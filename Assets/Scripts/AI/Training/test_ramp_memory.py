import copy
import hashlib
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from prepare_ramp_memory import BASELINE, BASELINE_SHA256, CONFIG, OLD_SHAPES, NEW_SHAPES, prepare
from prepare_branch_observation import make_policy, migrate_states


class RampMemoryTests(unittest.TestCase):
    @staticmethod
    def initial_state():
        import torch
        from mlagents.trainers import learn
        from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
        settings = learn.parse_command_line([CONFIG, "--run-id=TestRampMemory"]).behaviors["Explorer"]
        p = make_policy(settings, NEW_SHAPES); opt = TorchPPOOptimizer(p, settings)
        modules = {**p.get_modules(), **opt.get_modules()}
        return migrate_states(torch.load(BASELINE, map_location="cpu"),
            {k:v.state_dict() for k,v in modules.items()}, old_size=161, new_size=177,
            new_processor=6, preserve_step=True)[0]

    def test_exact_old_weights_zero_new_inputs_and_source_unchanged(self):
        import torch
        self.assertEqual(hashlib.sha256(Path(BASELINE).read_bytes()).hexdigest(), BASELINE_SHA256)
        old = torch.load(BASELINE, map_location="cpu"); new = self.initial_state()
        self.assertEqual(int(new['global_step']['_GlobalSteps__global_step'].item()), 5000036)
        self.assertEqual(new['Optimizer:value_optimizer']['state'], {})
        expanded = 0
        for module in ['Policy', 'Optimizer:critic', 'Module:Curiosity']:
            for key, value in old[module].items():
                actual = new[module][key]
                if value.shape != actual.shape:
                    self.assertTrue(key.endswith('_body_endoder.seq_layers.0.weight'))
                    self.assertTrue(torch.equal(value, actual[:, :161]))
                    self.assertEqual(actual[:, 161:].count_nonzero().item(), 0); expanded += 1
                else: self.assertTrue(torch.equal(value, actual), module+'/'+key)
        self.assertEqual(expanded, 3)

    def test_new_memory_inputs_receive_gradient_and_update(self):
        import torch
        from mlagents.trainers import learn
        from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
        settings = learn.parse_command_line([CONFIG, '--run-id=TestRampMemory']).behaviors['Explorer']
        p = make_policy(settings, NEW_SHAPES); opt = TorchPPOOptimizer(p, settings)
        p.actor.load_state_dict(self.initial_state()['Policy'], strict=True)
        x = [torch.randn(4, n) for n in NEW_SHAPES]
        y, _ = p.actor.network_body(x, memories=torch.zeros(1, 4, p.actor.memory_size))
        y.square().sum().backward()
        w = dict(p.actor.named_parameters())['network_body._body_endoder.seq_layers.0.weight']
        self.assertGreater(w.grad[:,161:].abs().sum().item(), 0)
        opt.optimizer.step(); self.assertGreater(w[:,161:].abs().sum().item(), 0)

    def test_export_and_counter(self):
        import onnx
        from prepare_ramp_memory import schema
        from train_phase2_terrain import resume_steps
        root = Path('results/Phase2E'); m = onnx.load(str(root/'Explorer.onnx')); onnx.checker.check_model(m)
        self.assertEqual(schema(m), NEW_SHAPES)
        s = json.loads((root/'run_logs/training_status.json').read_text())
        self.assertGreaterEqual(resume_steps(root/'Explorer/checkpoint.pt', s), 5000036)
        report = json.loads((root/'migration.json').read_text())
        self.assertLess(report['max_policy_and_value_encoding_error'], 1e-5)

    def test_existing_destination_is_not_overwritten(self):
        with self.assertRaisesRegex(ValueError, 'Destination exists'): prepare()

    def test_old_run_is_rejected(self):
        from mlagents.trainers import learn
        import train_phase2_terrain
        options = learn.parse_command_line([CONFIG, '--run-id=Phase2D', '--resume'])
        with patch.object(learn, 'parse_command_line', return_value=options), patch.object(learn, 'run_cli') as cli:
            with self.assertRaisesRegex(ValueError, 'Run mode differs'): train_phase2_terrain.main()
            cli.assert_not_called()

    def test_no_unapproved_budget_extension(self):
        from mlagents.trainers import learn
        import train_phase2_terrain
        options = learn.parse_command_line([CONFIG, '--run-id=Phase2E', '--resume'])
        s = json.loads(Path('results/Phase2E/run_logs/training_status.json').read_text())
        options.behaviors['Explorer'].max_steps = s['Explorer']['final_checkpoint']['steps']
        with patch.object(learn, 'parse_command_line', return_value=options), patch.object(learn, 'run_cli') as cli:
            with self.assertRaisesRegex(ValueError, 'Raise the budget explicitly'): train_phase2_terrain.main()
            cli.assert_not_called()

    def test_driver_preflight_with_mock_budget_never_launches_training(self):
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        from mlagents.trainers.training_status import GlobalTrainingStatus
        import train_phase2_terrain
        options = learn.parse_command_line([CONFIG, '--run-id=Phase2E', '--resume'])
        s = json.loads(Path('results/Phase2E/run_logs/training_status.json').read_text())
        options.behaviors['Explorer'].max_steps = s['Explorer']['final_checkpoint']['steps'] + 100
        previous = copy.deepcopy(GlobalTrainingStatus.saved_state)
        writers, manager_type = list(StatsReporter.writers), learn.EnvironmentParameterManager
        def fake(opts):
            self.assertIsNone(opts.behaviors['Explorer'].init_path)
            GlobalTrainingStatus.load_state('results/Phase2E/run_logs/training_status.json')
            manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            self.assertEqual(manager.get_current_samplers()['ramp_memory_observation'].value, 1)
            self.assertEqual(manager.get_current_samplers()['terrain_level'].value, s['terrain_level']['lesson_num'])
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, 'parse_command_line', return_value=options), patch.object(learn, 'run_cli', side_effect=fake) as cli:
                train_phase2_terrain.main(); cli.assert_called_once()
        finally:
            GlobalTrainingStatus.saved_state.clear(); GlobalTrainingStatus.saved_state.update(previous)
            StatsReporter.writers[:] = writers; learn.EnvironmentParameterManager = manager_type

    def test_memory_has_no_tags_oracle_actuator_or_reward(self):
        source = Path('Assets/Scripts/AI/RampMemorySensorComponent.cs').read_text()
        for forbidden in ['CompareTag(', '.tag', 'AIWFCTrainingAdapter', 'ExplorationArea', 'StairTraversalArea',
                          'AddReward(', 'EndEpisode(', 'motor.Move(', 'SetDestination(']:
            self.assertNotIn(forbidden, source)


if __name__ == '__main__': unittest.main()
