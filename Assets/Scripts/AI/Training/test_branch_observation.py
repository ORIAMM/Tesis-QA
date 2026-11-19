import copy
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from prepare_branch_observation import OLD_SHAPES, NEW_SHAPES, make_policy, migrate_states


class BranchObservationTests(unittest.TestCase):
    def test_migrated_state_is_exact_for_old_parameters_and_zero_for_new_inputs(self):
        import torch
        root = Path('results/BranchObservationInit/Phase2C')
        report = json.loads((root / 'migration.json').read_text())
        old = torch.load(report['source'], map_location='cpu')
        new = torch.load(root / 'Explorer-0.pt', map_location='cpu')
        self.assertEqual(report['new_shapes'], NEW_SHAPES)
        self.assertEqual(int(new['global_step']['_GlobalSteps__global_step'].item()), 0)
        self.assertEqual(new['Optimizer:value_optimizer']['state'], {})
        for module in ['Policy', 'Optimizer:critic', 'Module:Curiosity']:
            for key, tensor in old[module].items():
                actual = new[module][key]
                if tensor.shape != actual.shape:
                    self.assertTrue(key.endswith('_body_endoder.seq_layers.0.weight'))
                    self.assertTrue(torch.equal(tensor, actual[:, :117]))
                    self.assertEqual(actual[:, 117:].count_nonzero().item(), 0)
                else:
                    self.assertTrue(torch.equal(tensor, actual), f'{module}/{key}')

    def test_new_input_can_receive_gradients_and_optimizer_step(self):
        import torch
        from mlagents.trainers import learn
        from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
        settings = learn.parse_command_line([
            'Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml', '--run-id=UnitTestBranchObs']).behaviors['Explorer']
        policy = make_policy(settings, NEW_SHAPES)
        optimizer = TorchPPOOptimizer(policy, settings)
        state = torch.load('results/BranchObservationInit/Phase2C/Explorer-0.pt', map_location='cpu')
        policy.actor.load_state_dict(state['Policy'], strict=True)
        x = [torch.randn(4, n) for n in NEW_SHAPES]
        encoding, _ = policy.actor.network_body(x, memories=torch.zeros(1, 4, policy.actor.memory_size))
        encoding.square().sum().backward()
        weight = dict(policy.actor.named_parameters())['network_body._body_endoder.seq_layers.0.weight']
        self.assertGreater(weight.grad[:, 117:].abs().sum().item(), 0)
        optimizer.optimizer.step()
        self.assertGreater(weight[:, 117:].abs().sum().item(), 0)

    def test_incompatible_expansion_fails_instead_of_random_partial_load(self):
        import torch
        bad = {'Policy': {'weight': torch.zeros(1, 117)}}
        with self.assertRaisesRegex(ValueError, 'Unsupported shape'):
            migrate_states(bad, {'Policy': {'weight': torch.zeros(1, 149)}})

    def test_driver_starts_level_eight_with_observation_not_assistance(self):
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        from mlagents.trainers.training_status import GlobalTrainingStatus
        import train_phase2_terrain
        options = learn.parse_command_line([
            'Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml', '--run-id=UnitTestBranchObs'])
        previous = copy.deepcopy(GlobalTrainingStatus.saved_state)
        writers, manager_type = list(StatsReporter.writers), learn.EnvironmentParameterManager
        def fake_cli(opts):
            manager = learn.EnvironmentParameterManager(opts.environment_parameters)
            samplers = manager.get_current_samplers()
            self.assertEqual(samplers['terrain_level'].value, 8)
            self.assertEqual(samplers['completed_branch_assist'].value, 0)
            self.assertEqual(samplers['branch_memory_observation'].value, 1)
            self.assertEqual(GlobalTrainingStatus.saved_state['phase2_terrain_mode'],
                             {'initial_level': 8, 'completed_branch_assist': 0, 'branch_memory_observation': 1})
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, 'parse_command_line', return_value=options), patch.object(learn, 'run_cli', fake_cli):
                train_phase2_terrain.main()
        finally:
            GlobalTrainingStatus.saved_state.clear(); GlobalTrainingStatus.saved_state.update(previous)
            StatsReporter.writers[:] = writers; learn.EnvironmentParameterManager = manager_type

    def test_old_run_cannot_be_resumed_with_new_observations(self):
        from mlagents.trainers import learn
        import train_phase2_terrain
        options = learn.parse_command_line([
            'Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml', '--run-id=Phase2B', '--resume'])
        saved = {'phase2_terrain_mode': {'initial_level': 8, 'completed_branch_assist': 1}}
        with patch.object(learn, 'parse_command_line', return_value=options), \
             patch.object(Path, 'read_text', return_value=json.dumps(saved)), patch.object(learn, 'run_cli') as cli:
            with self.assertRaisesRegex(ValueError, 'Run mode differs'): train_phase2_terrain.main()
            cli.assert_not_called()

    def test_checkpoint_not_overwritten_and_agent_has_no_branch_action_override(self):
        from prepare_branch_observation import prepare
        with self.assertRaisesRegex(ValueError, 'Destination exists'):
            prepare('results/Phase2B/Explorer/Explorer-2000101.pt',
                    'results/BranchObservationInit/Phase2C',
                    'Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml')
        text = Path('Assets/Scripts/AI/ExplorationAgent.cs').read_text()
        self.assertNotIn('.Filter(', text)
        self.assertNotIn('AddComponent<AICompletedBranchAssist>', text)


if __name__ == '__main__':
    unittest.main()
