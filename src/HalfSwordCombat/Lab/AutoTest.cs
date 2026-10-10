using System;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;

namespace HalfSwordCombat.Lab
{
    /// <summary>
    /// Main menu -> Custom Battle -> a small infantry fight, with no mouse or keyboard input.
    /// Mirrors CustomBattleVM.ExecuteStart (decompiled reference) with fixed, repeatable choices.
    /// </summary>
    internal static class AutoTest
    {
        private static int _stage;

        public static void Tick()
        {
            GameStateManager gsm = GameStateManager.Current;
            string state = gsm?.ActiveState?.GetType().Name ?? "<none>";
            try
            {
                if (_stage == 0 && state == "InitialState")
                {
                    _stage = 1;
                    ProbeLog.Write("AutoTest: main menu reached, loading Custom Battle");
                    MBGameManager.StartNewGame(new CustomGameManager());
                }
                else if (_stage == 1 && state == "CustomBattleState")
                {
                    _stage = 2;
                    StartBattle();
                }
            }
            catch (Exception e)
            {
                _stage = 99;
                ProbeLog.Write("AutoTest FAILED in state " + state + ": " + e);
            }
        }

        private static void StartBattle()
        {
            // Put a hero on foot first: Half Sword combat is for infantry (run 3's commander_1 was mounted).
            BasicCharacterObject[] heroes = CustomBattleData.Characters
                .OrderBy(h => h.Equipment != null && h.Equipment[EquipmentIndex.Horse].IsEmpty ? 0 : 1)
                .ToArray();
            BasicCultureObject[] factions = CustomBattleData.Factions.ToArray();
            BasicCultureObject playerFaction = factions.FirstOrDefault(f => f.StringId == "vlandia") ?? factions[0];
            BasicCultureObject enemyFaction = factions.FirstOrDefault(f => f.StringId == "empire") ?? factions[factions.Length - 1];
            CustomBattleSceneData scene = CustomGame.Current.CustomBattleScenes
                .Where(s => !s.IsSiegeMap && !s.IsVillageMap && !s.IsLordsHallMap)
                .OrderBy(s => s.SceneID == "battle_terrain_029" ? 0 : 1)
                .First();

            // Infantry only, 6 v 6 (the player counts as one of the six).
            int[] playerNumbers = { 5, 0, 0, 0 };
            int[] enemyNumbers = { 5, 0, 0, 0 };
            // Sergeant mode needs a separate general on the player's side: GeneralsAndCaptainsAssignmentLogic calls
            // playerSideGeneralCharacter.GetName() unconditionally (NRE with null, seen in lab run 1).
            BasicCharacterObject general = heroes.Length > 2 ? heroes[2] : heroes[1];
            CustomBattleCombatant[] parties = CustomBattleHelper.GetCustomBattleParties(
                heroes[0], general, heroes[1], playerFaction, playerNumbers, null, enemyFaction, enemyNumbers, null, true);

            CustomBattleData data = CustomBattleHelper.PrepareBattleData(
                heroes[0], general, parties[0], parties[1], CustomBattlePlayerSide.Attacker, CustomBattlePlayerType.Sergeant,
                "Battle", scene.SceneID, "summer", 12f, null, null, null, 0, false, scene.ForcedSceneLevel);

            ProbeLog.Write("AutoTest: starting battle scene=" + scene.SceneID + " player=" + heroes[0].StringId +
                           " vs " + heroes[1].StringId + " (" + playerFaction.StringId + " v " + enemyFaction.StringId + ")");
            ProbeLog.Write("AutoTest: " + LabEnv.Describe());
            CustomBattleHelper.StartGame(data);
        }
    }
}
