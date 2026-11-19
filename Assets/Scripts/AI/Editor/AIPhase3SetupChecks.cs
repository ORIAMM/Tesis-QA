using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.MLAgents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    // Read-only integration check on the isolated copy; never saves scene/model.
    public static class AIPhase3SetupChecks
    {
        public static void ValidateBatch() => ValidateProfile(1);
        public static void ValidateSplitBatch() => ValidateProfile(2);
        private static void ValidateProfile(int rewardRevision)
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated .ai-eval project.");
            CommunicatorFactory.Enabled = false;
            AIForwardTerrainChecks.ValidateScenes();
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity", OpenSceneMode.Single);
            var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
            var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
            var env = UnityEngine.Object.FindAnyObjectByType<AITrainingEnvironment>();
            var selectedProfile = new SerializedObject(env);
            if (rewardRevision == 2)
                Check(selectedProfile.FindProperty("phase3RewardRevision").intValue == 2,
                    "Source scene and split profile are not aligned.");
            selectedProfile.FindProperty("phase3RewardRevision").intValue = rewardRevision;
            selectedProfile.ApplyModifiedPropertiesWithoutUndo(); // Unsaved isolated copy only.
            Check(agent.MaxStep == 12000, "Episode MaxStep must be 12000.");
            Check(env.Phase == AITrainingPhase.ComplexRouting, "Wrong training phase.");
            var generator = UnityEngine.Object.FindAnyObjectByType<TesisQA.WFC.WFCGenerator>();
            Vector3Int size = new SerializedObject(generator).FindProperty("gridSize").vector3IntValue;
            Check(size.x == 24 && size.z == 24, "Wrong Phase3 map size.");
            // Batch editor checks do not invoke MonoBehaviour.Awake automatically.
            typeof(AITrainingEnvironment).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(env, null);
            agent.Initialize();
            object channel = typeof(EnvironmentParameters).GetField("m_Channel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Academy.Instance.EnvironmentParameters);
            var parameters = (Dictionary<string, Func<float>>)channel.GetType()
                .GetField("m_Parameters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(channel);
            parameters["terrain_curriculum"] = () => 1f;
            parameters["phase3_curriculum"] = () => 1f;
            parameters["phase3_reward_revision"] = () => rewardRevision;
            parameters["terrain_driver"] = () => 1f;
            parameters["terrain_map_seed"] = () => 33003f;
            parameters["stair_search_shaping"] = () => 0f;
            parameters["terrain_level"] = () => 8f;
            // Episode 8 would rehearse transfer level 7 in Phase 2: explicitly
            // prove it stays unguided balanced coverage in Phase 3.
            Check(adapter.PrepareEpisode(8), "Unable to build usable two-floor map.");
            Check(adapter.TerrainLevel == 8 && adapter.TerrainEpisodeLevel == 8
                && !adapter.TerrainRehearsal && !adapter.IsFloorTransferLesson,
                "Phase3 level 8 incorrectly rehearses guided transfer.");
            Check(adapter.IsTwoFloorLesson && !adapter.StairGuideEnabled, "Wrong floor/guide mode.");
            Check(new SerializedObject(generator).FindProperty("gridSize").vector3IntValue.y == 2,
                "Generator did not switch to two actual floors.");
            Check(Mathf.Approximately(adapter.GraduatedFloorCoverageTarget, 0.1f), "Wrong 10% target.");
            parameters["terrain_level"] = () => 9f;
            Check(adapter.PrepareEpisode(9), "20% rehearsal map failed.");
            Check(adapter.TerrainEpisodeLevel == 8 && adapter.TerrainRehearsal, "Missing 10% rehearsal.");
            Check(adapter.PrepareEpisode(10), "20% main episode map failed.");
            Check(adapter.TerrainEpisodeLevel == 9 && !adapter.TerrainRehearsal
                && Mathf.Approximately(adapter.GraduatedFloorCoverageTarget, 0.2f), "Wrong 20% main target.");
            agent.OnEpisodeBegin();
            Check(agent.enabled && agent.MaxStep == 12000, "Agent rejected Phase 3 curriculum/start.");
            Check(env.Phase3RewardRevision == rewardRevision && env.IsTwoFloorCoverageLesson, "Phase3 two-floor guard not enabled.");
            Check(Mathf.Approximately((float)typeof(AITrainingEnvironment).GetField("episodeNoNewAreaTimeout",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(env), 25f), "Phase3 must use 25-second no-progress timeout.");
            ValidateRewardMath();
            ValidateSplitRewardMath();
            ValidateRewardVisits(env, agent);
            Debug.Log($"AI PHASE3 SETUP PASSED: 177 schema, 24x24 two floors, 12000 steps, targets 10/20, reward revision {rewardRevision}, timeout 25, symmetric/normalized rewards, split floor budget checked, no revisit reward, legacy opt-out, episode spawn. No training or scene saves.");
        }

        private static void ValidateRewardMath()
        {
            foreach (float target in new[] {0.1f, 0.2f})
            {
                Check(Mathf.Approximately(AIPhase3CoverageReward.ExplorationTotal(target, 0f, target),
                    AIPhase3CoverageReward.ExplorationTotal(0f, target, target)), "Reward is biased toward one floor.");
                Check(AIPhase3CoverageReward.BalanceTotal(target * 2f, 0f, target) == 0f,
                    "Exploring one floor awarded balance reward.");
                Check(Mathf.Approximately(AIPhase3CoverageReward.BalanceTotal(target, target, target), 1f),
                    "Balance budget differs between lessons.");
                Check(Mathf.Approximately(AIPhase3CoverageReward.ExplorationTotal(1f, 1f, target), 0.25f),
                    "Exploration budget can be farmed above its cap.");
            }
            Check(AIPhase3CoverageReward.Increment(1f, 1f) == 0f, "Repeated state awards reward.");
            Check(AIPhase3CoverageReward.BalanceTotal(.3f, .1f, .1f) < 1f
                && Mathf.Approximately(AIPhase3CoverageReward.BalanceTotal(.3f, .2f, .1f), 1f),
                "Reward saturated before the floor-difference requirement was met.");
            for (int i = 0; i <= 50; i++)
                for (int j = 0; j <= 50; j++)
                {
                    float lower = i * .02f, upper = j * .02f;
                    if (AIPhase3CoverageReward.BalanceTotal(lower, upper, .1f) >= .99999f)
                        Check(AITerrainCurriculum.BalancedCoverage(.1f, lower, upper, .1f),
                            "Full reward can be earned without balanced coverage.");
                }
            float previous = 0f, earned = 0f;
            for (int i = 1; i <= 100; i++)
            {
                float total = AIPhase3CoverageReward.BalanceTotal(i * .001f, .1f, .1f);
                earned += AIPhase3CoverageReward.Increment(total, previous); previous = total;
            }
            Check(Mathf.Abs(earned - 1f) < .00001f, "Reward depends on the number of area events.");
        }

        private static void ValidateRewardVisits(AITrainingEnvironment env, ExplorationAgent agent)
        {
            // Synthetic areas exercise the ACTUAL VisitArea path without physics,
            // tags, a planner or feeding coverage counters to observations.
            var oldAreasField = typeof(AITrainingEnvironment).GetField("explorationAreas", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = (ExplorationArea[])oldAreasField.GetValue(env);
            var visited = (HashSet<ExplorationArea>)typeof(AITrainingEnvironment)
                .GetField("visitedAreas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(env);
            int profile = env.Phase3RewardRevision;
            var targetField = typeof(AITrainingEnvironment).GetField("episodeTargetCoverage", BindingFlags.Instance | BindingFlags.NonPublic);
            float oldTarget = (float)targetField.GetValue(env);
            var testRoot = new GameObject("Temporary reward test areas");
            testRoot.transform.position = Vector3.one * 20000f;
            var areas = new ExplorationArea[20];
            for (int i = 0; i < areas.Length; i++)
            {
                var obj = new GameObject("Area " + i); obj.transform.SetParent(testRoot.transform, false);
                obj.AddComponent<BoxCollider>(); areas[i] = obj.AddComponent<ExplorationArea>();
                areas[i].ConfigureFloor(i < 10 ? 0 : 1);
            }
            try
            {
                oldAreasField.SetValue(env, areas); visited.Clear(); targetField.SetValue(env, .2f);
                env.VisitArea(agent, areas[0]);
                float first = PrivateFloat(env, "awardedAreaReward");
                Check(first > 0f && PrivateFloat(env, "awardedBalancedCoverageReward") == 0f,
                    "Single-floor progress incorrectly awarded balance.");
                env.VisitArea(agent, areas[0]);
                Check(Mathf.Approximately(first, PrivateFloat(env, "awardedAreaReward")), "Revisit generated exploration reward.");
                if (profile == 2)
                {
                    env.VisitArea(agent, areas[1]); // First floor reaches 20% target.
                    Check(Mathf.Approximately(PrivateFloat(env, "awardedAreaReward"), .125f), "First floor budget differs.");
                    env.VisitArea(agent, areas[2]); // 30% must not consume the other floor's budget.
                    Check(Mathf.Approximately(PrivateFloat(env, "awardedAreaReward"), .125f), "Overexploration consumed the other floor budget.");
                }
                env.VisitArea(agent, areas[10]);
                Check(PrivateFloat(env, "awardedBalancedCoverageReward") > 0f,
                    "Catching up the other floor received no reward.");
                Check(PrivateFloat(env, "awardedUpperFloorAreaBonus") == 0f, "Legacy upper-floor bonus leaked.");
                if (profile == 2)
                {
                    Check(Mathf.Approximately(PrivateFloat(env, "awardedAreaReward"), .1875f), "Other floor lost its reserved reward.");
                    float earned = PrivateFloat(env, "awardedAreaReward");
                    env.VisitArea(agent, areas[10]);
                    Check(Mathf.Approximately(earned, PrivateFloat(env, "awardedAreaReward")), "Other-floor revisit generated reward.");
                }
                var revision = new SerializedObject(env); revision.FindProperty("phase3RewardRevision").intValue = 0;
                revision.ApplyModifiedPropertiesWithoutUndo();
                Check(!env.IsTwoFloorCoverageLesson, "Legacy Phase3 timeout profile was changed.");
                revision.FindProperty("phase3RewardRevision").intValue = profile; revision.ApplyModifiedPropertiesWithoutUndo();
            }
            finally { oldAreasField.SetValue(env, original); targetField.SetValue(env, oldTarget); UnityEngine.Object.DestroyImmediate(testRoot); }
        }

        private static void ValidateSplitRewardMath()
        {
            foreach (float target in new[] {.1f, .2f})
            {
                Check(Mathf.Approximately(AIPhase3CoverageReward.SplitExplorationTotal(2f * target, 0f, target), .125f),
                    "A single floor consumed more than half the budget.");
                Check(Mathf.Approximately(AIPhase3CoverageReward.ExplorationTotal(2f * target, 0f, target), .25f),
                    "Historical revision 1 changed.");
                Check(Mathf.Approximately(AIPhase3CoverageReward.SplitExplorationTotal(target, target, target), .25f),
                    "Both-floor target does not exactly exhaust the shared cap.");
                float previous = 0f, earned = 0f;
                for (int i = 0; i <= 100; i++)
                {
                    float total = AIPhase3CoverageReward.SplitExplorationTotal(1f, i * .01f, target);
                    earned += AIPhase3CoverageReward.Increment(total, previous); previous = total;
                }
                Check(Mathf.Approximately(earned, .25f), "Reward cap depends on sample frequency.");
                for (int i = 0; i <= 50; i++)
                    for (int j = 0; j <= 50; j++)
                    {
                        float lower = i * .02f, upper = j * .02f;
                        float total = AIPhase3CoverageReward.SplitExplorationTotal(lower, upper, target);
                        Check(total >= 0f && total <= .250001f, "Split reward exceeded total budget.");
                        Check(Mathf.Approximately(total, AIPhase3CoverageReward.SplitExplorationTotal(upper, lower, target)),
                            "Split exploration favors a floor.");
                        Check(AIPhase3CoverageReward.SplitExplorationTotal(lower + .01f, upper, target) + 1e-6f >= total,
                            "More coverage reduced split reward.");
                    }
            }
            Check(AIPhase3CoverageReward.SplitExplorationTotal(-1f, -1f, .1f) == 0f, "Negative coverage generated reward.");
        }

        private static float PrivateFloat(AITrainingEnvironment env, string name) => (float)typeof(AITrainingEnvironment)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(env);

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
