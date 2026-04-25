// This file is 100% inspired by Dynamic maps and is used
// under the MIT License as provided by Dynamic maps

using System.Collections.Generic; // HashSet
using System.Text.RegularExpressions; // Regex
using Comfort.Common; // Singleton
using EFT; // IPlayer

namespace flir.enemymarkers
{
    public static class GameUtils
    {
        private static readonly HashSet<WildSpawnType> TrackedGuards = new HashSet<WildSpawnType>
        {
            WildSpawnType.followerBully,
            WildSpawnType.followerGluharAssault,
            WildSpawnType.followerGluharSecurity,
            WildSpawnType.followerGluharScout,
            WildSpawnType.followerGluharSnipe,
            WildSpawnType.followerSanitar,
            WildSpawnType.followerTagilla,
            WildSpawnType.followerZryachiy,
            WildSpawnType.followerBoar,
            WildSpawnType.followerBoarClose1,
            WildSpawnType.followerBoarClose2,
            WildSpawnType.followerKolontayAssault,
            WildSpawnType.followerKolontaySecurity,
            WildSpawnType.pmcBot
        };

        private static readonly HashSet<WildSpawnType> TrackedBosses = new HashSet<WildSpawnType>
        {
            WildSpawnType.bossBoar, // Kaban
            WildSpawnType.bossBully, // Reshala
            WildSpawnType.bossGluhar, // Glukhar
            WildSpawnType.bossKilla,
            WildSpawnType.bossKnight,
            WildSpawnType.followerBigPipe,
            WildSpawnType.followerBirdEye,
            WildSpawnType.bossKolontay,
            WildSpawnType.bossKojaniy, // Shturman
            WildSpawnType.bossSanitar,
            WildSpawnType.bossTagilla,
            WildSpawnType.bossPartisan,
            WildSpawnType.bossZryachiy,
            WildSpawnType.gifter, // Santa
            WildSpawnType.arenaFighterEvent, // Blood Hounds
            WildSpawnType.sectantPriest, // Cultist Priest
            (WildSpawnType)199, // Legion
            (WildSpawnType)801, // Punisher
        };

        public static bool IsHeadlessClient(this IPlayer player)
        {
            const string pattern = @"^headless_[a-fA-F0-9]{24}$";
            return Regex.IsMatch(player.Profile.GetCorrectedNickname(), pattern);
        }

        public static bool IsInRaid()
        {
            var game = Singleton<AbstractGame>.Instance;
            var botGame = Singleton<IBotGame>.Instance;

            return (game != null && game.InRaid)
                   || (botGame != null && botGame.Status != GameStatus.Stopped
                                       && botGame.Status != GameStatus.Stopping
                                       && botGame.Status != GameStatus.SoftStopping);
        }

        public static Player GetMainPlayer()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            return gameWorld?.MainPlayer;
        }

        public static bool IsGroupedWithMainPlayer(this IPlayer player)
        {
            var mainPlayerGroupId = GetMainPlayer().GroupId;
            return !string.IsNullOrEmpty(mainPlayerGroupId) && player.GroupId == mainPlayerGroupId;
        }

        public static bool IsTrackedBoss(this IPlayer player)
        {
            return player.Profile.Side == EPlayerSide.Savage &&
                   TrackedBosses.Contains(player.Profile.Info.Settings.Role);
        }

        public static bool IsTrackedGuard(this IPlayer player)
        {
            return player.Profile.Side == EPlayerSide.Savage &&
                   TrackedGuards.Contains(player.Profile.Info.Settings.Role);
        }

        public static bool IsPMC(this IPlayer player)
        {
            return player.Profile.Side == EPlayerSide.Bear || player.Profile.Side == EPlayerSide.Usec;
        }

        public static bool IsScav(this IPlayer player)
        {
            return player.Profile.Side == EPlayerSide.Savage;
        }

        public static bool IsMainPlayerScav()
        {
            var mainPlayer = GetMainPlayer();
            return mainPlayer != null && mainPlayer.IsScav();
        }
    }
}
