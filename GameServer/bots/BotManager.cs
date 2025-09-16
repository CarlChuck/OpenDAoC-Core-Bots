using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using log4net;

namespace DOL.GS
{
    public class BotManager
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly ConcurrentDictionary<string, GameBot> ActiveBots = new(); // ← WE UPGRADE TO ConcurrentDictionary
        // Lock no longer needed — ConcurrentDictionary is lock-free for most ops

        public const int MAX_BOTS_PER_PLAYER = 15;
        public const int FOLLOW_DISTANCE = 150;
        public const int MAX_FOLLOW_DISTANCE = 400;

        /// <summary>
        /// Create a new bot with specified parameters
        /// </summary>
        public static GameBot CreateBot(GamePlayer owner, string name, byte classId, byte raceId, byte genderId)
        {
            if (owner == null) return null;

            var currentBots = GetBotsForOwner(owner).Count();
            if (currentBots >= MAX_BOTS_PER_PLAYER)
            {
                owner.Out.SendMessage($"You can only control {MAX_BOTS_PER_PLAYER} bots at a time.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return null;
            }

            // Convert classId to class name (TODO: implement proper class mapping)
            string className = GetClassNameById(classId);
            
            var bot = new GameBot(owner, className, name);
            ActiveBots[bot.InternalID] = bot;
            
            // Set additional properties based on parameters
            bot.RaceId = raceId;
            bot.GenderId = genderId;
            bot.ClassId = classId;
            
            log.InfoFormat("Bot {0} created for player {1}", bot.InternalID, owner.Name);
            return bot;
        }

        private static string GetClassNameById(byte classId)
        {
            // TODO: Implement proper class ID to name mapping
            return classId switch
            {
                1 => "Fighter",
                2 => "Cleric",
                3 => "Wizard",
                4 => "Scout",
                _ => "Fighter"
            };
        }

        public static bool RemoveBot(GameBot bot)
        {
            if (bot == null) return false;

            if (ActiveBots.TryRemove(bot.InternalID, out _))
            {
                bot.RemoveFromWorld();
                bot.Delete();
                log.InfoFormat("Bot {0} removed", bot.InternalID);
                return true;
            }
            return false;
        }

        public static IEnumerable<GameBot> GetBotsForOwner(GamePlayer owner)
        {
            if (owner == null) return Enumerable.Empty<GameBot>();
            return ActiveBots.Values.Where(b => b.Owner == owner);
        }

        public static void CleanupPlayerBots(GamePlayer owner)
        {
            if (owner == null) return;
            var bots = GetBotsForOwner(owner).ToList();
            foreach (var bot in bots) RemoveBot(bot);
        }

        /// <summary>
        /// Load a bot from database by name for the owner
        /// </summary>
        public static GameBot LoadBotByName(GamePlayer owner, string botName)
        {
            return BotDatabase.LoadBotByName(owner, botName);
        }

        /// <summary>
        /// Spawn a bot into the world
        /// </summary>
        public static void SpawnBot(GameBot bot)
        {
            if (bot != null)
            {
                bot.AddToWorld();
                ActiveBots[bot.InternalID] = bot;
                log.InfoFormat("Bot {0} spawned", bot.InternalID);
            }
        }

        /// <summary>
        /// Get saved bots for owner from database
        /// </summary>
        public static IEnumerable<GameBot> GetSavedBotsForOwner(GamePlayer owner)
        {
            var profiles = BotDatabase.GetSavedBots(owner);
            return profiles.Select(p => BotDatabase.LoadBot(owner, p.BotId)).Where(b => b != null);
        }

        /// <summary>
        /// Delete a bot permanently from database
        /// </summary>
        public static void DeleteBot(GameBot bot)
        {
            if (bot != null)
            {
                RemoveBot(bot);
                BotDatabase.DeleteBot(bot);
                log.InfoFormat("Bot {0} deleted permanently", bot.InternalID);
            }
        }

        /// <summary>
        /// Despawn a bot (remove from world but keep in database)
        /// </summary>
        public static void DespawnBot(GameBot bot)
        {
            if (bot != null)
            {
                bot.RemoveFromWorld();
                ActiveBots.TryRemove(bot.InternalID, out _);
                // TODO: Set is_active=false in database
                log.InfoFormat("Bot {0} despawned", bot.InternalID);
            }
        }

        /// <summary>
        /// Get bot by name for owner (active bots only)
        /// </summary>
        public static GameBot GetBotByName(GamePlayer owner, string botName)
        {
            if (owner == null || string.IsNullOrEmpty(botName)) return null;
            
            return GetBotsForOwner(owner).FirstOrDefault(b => 
                string.Equals(b.Name, botName, StringComparison.OrdinalIgnoreCase));
        }
    }
}