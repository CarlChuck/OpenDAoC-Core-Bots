using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using log4net;
using DOL.Database;
using DOL.GS.Database;

namespace DOL.GS
{
    /// <summary>
    /// Database operations for bot system
    /// </summary>
    public static class BotDatabase
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Save bot to database
        /// </summary>
        public static bool SaveBot(GameBot bot)
        {
            if (bot == null) return false;

            try
            {
                var profile = new BotProfile
                {
                    BotId = bot.DatabaseID,
                    OwnerCharacterID = bot.Owner.ObjectId,
                    Name = bot.Name,
                    ClassId = bot.ClassId,
                    RaceId = bot.RaceId,
                    GenderId = bot.GenderId,
                    Level = (byte)bot.Level,
                    IsActive = true
                };

                if (bot.DatabaseID == 0)
                {
                    // New bot - add to database
                    GameServer.Database.AddObject(profile);
                    bot.DatabaseID = profile.BotId;
                }
                else
                {
                    // Existing bot - save changes
                    GameServer.Database.SaveObject(profile);
                }

                // Save default settings
                SaveBotSettings(bot);

                log.InfoFormat("Bot {0} saved to database with ID {1}", bot.Name, bot.DatabaseID);
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Error saving bot {bot.Name}: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Load bot from database
        /// </summary>
        public static GameBot LoadBot(GamePlayer owner, long botId)
        {
            if (owner == null) return null;

            try
            {
                var profile = GameServer.Database.FindObjectByKey<BotProfile>(botId);
                if (profile == null || profile.OwnerCharacterID != owner.ObjectId)
                    return null;

                // Create GameBot from profile
                var bot = new GameBot(owner, GetClassNameById(profile.ClassId), profile.Name)
                {
                    DatabaseID = profile.BotId,
                    ClassId = profile.ClassId,
                    RaceId = profile.RaceId,
                    GenderId = profile.GenderId,
                    Level = profile.Level
                };

                // Load settings
                LoadBotSettings(bot);

                log.InfoFormat("Bot {0} loaded from database", bot.Name);
                return bot;
            }
            catch (Exception ex)
            {
                log.Error($"Error loading bot ID {botId}: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Load bot by name
        /// </summary>
        public static GameBot LoadBotByName(GamePlayer owner, string botName)
        {
            if (owner == null || string.IsNullOrEmpty(botName)) return null;

            try
            {
                var profiles = GameServer.Database.SelectObjects<BotProfile>(
                    "OwnerCharacterID = @OwnerID AND Name = @Name",
                    new QueryParameter("@OwnerID", owner.ObjectId),
                    new QueryParameter("@Name", botName));

                var profile = profiles.FirstOrDefault();
                if (profile == null) return null;

                return LoadBot(owner, profile.BotId);
            }
            catch (Exception ex)
            {
                log.Error($"Error loading bot {botName}: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Get all saved bots for owner
        /// </summary>
        public static List<BotProfile> GetSavedBots(GamePlayer owner)
        {
            if (owner == null) return new List<BotProfile>();

            try
            {
                return GameServer.Database.SelectObjects<BotProfile>(
                    "OwnerCharacterID = @OwnerID",
                    new QueryParameter("@OwnerID", owner.ObjectId)).ToList();
            }
            catch (Exception ex)
            {
                log.Error($"Error getting saved bots for {owner.Name}: {ex.Message}", ex);
                return new List<BotProfile>();
            }
        }

        /// <summary>
        /// Delete bot from database
        /// </summary>
        public static bool DeleteBot(GameBot bot)
        {
            if (bot == null || bot.DatabaseID == 0) return false;

            try
            {
                var profile = GameServer.Database.FindObjectByKey<BotProfile>(bot.DatabaseID);
                if (profile != null)
                {
                    GameServer.Database.DeleteObject(profile);
                }

                // Delete settings
                var settings = GameServer.Database.FindObjectByKey<BotSettings>(bot.DatabaseID);
                if (settings != null)
                {
                    GameServer.Database.DeleteObject(settings);
                }

                log.InfoFormat("Bot {0} deleted from database", bot.Name);
                return true;
            }
            catch (Exception ex)
            {
                log.Error($"Error deleting bot {bot.Name}: {ex.Message}", ex);
                return false;
            }
        }

        private static void SaveBotSettings(GameBot bot)
        {
            try
            {
                var settings = GameServer.Database.FindObjectByKey<BotSettings>(bot.DatabaseID) ?? new BotSettings
                {
                    BotId = bot.DatabaseID
                };

                settings.FollowDistance = BotManager.FOLLOW_DISTANCE;
                settings.CombatMode = "Assist";
                settings.HealThreshold = 50;
                settings.PreferredTarget = "Owner";

                if (GameServer.Database.FindObjectByKey<BotSettings>(bot.DatabaseID) == null)
                {
                    GameServer.Database.AddObject(settings);
                }
                else
                {
                    GameServer.Database.SaveObject(settings);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error saving bot settings for {bot.Name}: {ex.Message}", ex);
            }
        }

        private static void LoadBotSettings(GameBot bot)
        {
            try
            {
                var settings = GameServer.Database.FindObjectByKey<BotSettings>(bot.DatabaseID);
                if (settings != null)
                {
                    // TODO: Apply settings to bot
                    log.Debug($"Loaded settings for bot {bot.Name}");
                }
            }
            catch (Exception ex)
            {
                log.Error($"Error loading bot settings for {bot.Name}: {ex.Message}", ex);
            }
        }

        private static string GetClassNameById(byte classId)
        {
            return classId switch
            {
                1 => "Fighter",
                2 => "Cleric",
                3 => "Wizard",
                4 => "Scout",
                _ => "Fighter"
            };
        }
    }
}