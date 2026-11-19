using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.MLAgents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class AITerrainCurriculumChecks
    {
        public static void Validate()
        {
            for (int level = 0; level <= AITerrainCurriculum.LastLevel; level++)
            {
                int replay = 0;
                for (int episode = 0; episode < 10; episode++)
                    if (AITerrainCurriculum.IsRehearsal(level, episode)) replay++;
                Check(replay == (level == 0 || level == 3 ? 0 : 2), "Rehearsal percentage changed.");
                Check(AITerrainCurriculum.EpisodeLevel(level, 8) == level - (replay > 0 ? 1 : 0), "Replay skipped a level.");
                if (level < 3) Check(AITerrainCurriculum.Stage(level) == 0, "Single-floor stage changed.");
                else if (level < 8) Check(AITerrainCurriculum.IsTransfer(level), "Transfer mapping changed.");
                else Check(AITerrainCurriculum.Stage(level) == 4, "Balanced coverage stage changed.");
            }
            Check(Mathf.Approximately(AITerrainCurriculum.Coverage(0), 0.2f)
                && Mathf.Approximately(AITerrainCurriculum.Coverage(2), 0.4f)
                && Mathf.Approximately(AITerrainCurriculum.Coverage(8), 0.1f)
                && Mathf.Approximately(AITerrainCurriculum.Coverage(11), 0.4f), "Coverage targets changed.");
            Check(AITerrainCurriculum.MaxTiles(3) == 0 && AITerrainCurriculum.MaxTiles(7) == 4
                && AITerrainCurriculum.YawRange(3) == 30f && AITerrainCurriculum.YawRange(7) == 180f,
                "Distance/yaw progression changed.");
            Check(!AITerrainCurriculum.BalancedCoverage(0.2f, 0.4f, 0f, 0.1f)
                && !AITerrainCurriculum.BalancedCoverage(0.2f, 0.5f, 0.2f, 0.1f)
                && AITerrainCurriculum.BalancedCoverage(0.2f, 0.3f, 0.2f, 0.1f),
                "Coverage must hold per floor/episode, including the exact ten-point boundary.");
            Debug.Log("AI TERRAIN CURRICULUM CHECKS passed: 12 levels, coverage, distance/yaw, replay.");
        }

        // Batch diagnostic only: populate the SAME environment-parameter map as
        // the side channel, without changing ML-Agents or connecting a trainer.
        public static void ValidateMaps()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Map preflight requires the isolated batch project.");
            Validate();
            CommunicatorFactory.Enabled = false;
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase2_SmallWFC.unity", OpenSceneMode.Single);
            var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
            var parameters = Academy.Instance.EnvironmentParameters;
            object channel = typeof(EnvironmentParameters).GetField("m_Channel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(parameters);
            var values = (Dictionary<string, Func<float>>)channel.GetType().GetField("m_Parameters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(channel);
            var saved = new Dictionary<string, Func<float>>(values);
            try
            {
                values["terrain_curriculum"] = () => 1f;
                values["terrain_driver"] = () => 1f;
                values["graduated_transfer"] = () => 1f;
                values["stair_search_shaping"] = () => 1f;
                for (int level = 0; level <= AITerrainCurriculum.LastLevel; level++)
                {
                    int captured = level;
                    values["terrain_level"] = () => captured;
                    Check(adapter.PrepareEpisode(level * 40), "Could not prepare map at level " + level);
                    Check(adapter.TerrainLevel == level && !adapter.TerrainRehearsal, "Wrong primary level.");
                    Check(adapter.IsTwoFloorLesson == (level >= 3), "Wrong number of floors.");
                    for (int direction = 0; direction < 2; direction++)
                    {
                        Check(adapter.PrepareEpisode(level * 40 + direction), "Could not prepare direction.");
                        Check(adapter.TryChooseSpawnPair(new System.Random(500 + level + direction), 6f, 0f,
                            out Vector3 start, out Vector3 target), "No spawn pair at level " + level);
                        Check(!adapter.StairGuideEnabled, "Terrain branch leaked a stair direction guide.");
                        if (AITerrainCurriculum.IsTransfer(level))
                        {
                            Check((target.y > start.y) == (direction == 0), "Up/down sampling is not balanced.");
                            Check(adapter.LastSpawnStairSteps <= AITerrainCurriculum.MaxTiles(level), "Spawn exceeded its tile radius.");
                            Check(adapter.TryGetCurriculumSpawnRotation(new System.Random(700), out _), "Missing graduated initial yaw.");
                        }
                    }
                    Check(adapter.PrepareEpisode(level * 40 + 8), "Could not prepare replay.");
                    Check(adapter.TerrainRehearsal == AITerrainCurriculum.IsRehearsal(level, 8), "Replay mismatch.");
                    Debug.Log("AI TERRAIN MAP CHECK passed: level " + level + "; primary/replay, floors, spawn, directions, guide off.");
                }
            }
            finally
            {
                values.Clear();
                foreach (var entry in saved) values.Add(entry.Key, entry.Value);
            }
        }

        public static void ValidateAll()
        {
            ValidateMaps();
            AIStairSearchChecks.Validate();
            AINavigationMotionChecks.Validate();
            AIStairPracticeChecks.Validate();
            AINearSearchChecks.Validate();
            AIMixedSearchChecks.Validate();
            AIExplorationProgressChecks.Validate();
            AIForwardTerrainChecks.Validate();
            Debug.Log("AI TERRAIN FULL PREFLIGHT passed, including legacy checks and terrain geometry.");
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
