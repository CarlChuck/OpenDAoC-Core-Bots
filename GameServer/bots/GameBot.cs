using System;
using System.Numerics;
using log4net;
using DOL.Database;
using DOL.Events;

namespace DOL.GS
{
    public class GameBot : GameNPC
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(GameBot));

    public GamePlayer Owner { get; private set; }
    public string ClassName { get; private set; }
    public string InternalID { get; private set; }
    public BotRole Role { get; private set; }
    
    // Additional properties for database storage
    public byte ClassId { get; set; }
    public byte RaceId { get; set; }
    public byte GenderId { get; set; }

    private BotAI _botAI;
    private DateTime _lastActionTime = DateTime.MinValue;

    public GameBot(GamePlayer owner, string className, string name = null) : base()
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ClassName = className;
        InternalID = Guid.NewGuid().ToString();
        Role = DetermineBotRole(className);

        Name = string.IsNullOrEmpty(name) ? $"{owner.Name}'s {className} Bot" : name;
        Level = owner.Level;
        Realm = owner.Realm;
        Model = GetClassModel(className);

        _botAI = CreateBotAI(Role);

        InitializeBotStats();
        EquipBot();

        GameEventMgr.AddHandler(Owner, GamePlayerEvent.Quit, new GamePlayerEvent.PlayerQuitCallback(OnOwnerQuit));
    }

    private BotRole DetermineBotRole(string className)
    {
        return className switch
        {
            "Cleric" or "Druid" or "Healer" => BotRole.Healer,
            "Armsman" or "Hero" or "Warrior" => BotRole.Tank,
            "Wizard" or "Eldritch" or "Runemaster" => BotRole.Caster,
            "Scout" or "Ranger" or "Hunter" => BotRole.Ranged,
            _ => BotRole.Melee
        };
    }

    private BotAI CreateBotAI(BotRole role) => role switch
    {
        BotRole.Healer => new BotHealerAI(this),
        BotRole.Tank => new BotTankAI(this),
        BotRole.Caster => new BotCasterAI(this),
        BotRole.Ranged => new BotRangedAI(this),
        _ => new BotMeleeAI(this)
    };

    public override void Think()
    {
        if (!IsAlive || Owner == null || !Owner.IsAlive) return;

        // Throttle AI ticks to reduce CPU load
        if ((DateTime.Now - _lastActionTime).TotalMilliseconds < 500) return;
        _lastActionTime = DateTime.Now;

        base.Think();
        _botAI?.Tick();
    }

    private void OnOwnerQuit(GamePlayer player) => BotManager.RemoveBot(this);

    public override void Delete()
    {
        GameEventMgr.RemoveHandler(Owner, GamePlayerEvent.Quit, new GamePlayerEvent.PlayerQuitCallback(OnOwnerQuit));
        base.Delete();
    }

    /// <summary>
    /// Database ID for persistence (set when loaded from DB)
    /// </summary>
    public long DatabaseID { get; set; }
    
    /// <summary>
    /// Flag to enable/disable AI processing
    /// </summary>
    public bool IsAIEnabled { get; set; } = true;

    private void InitializeBotStats()
    {
        // Set basic stats based on owner's level and class
        // TODO: Implement proper stat calculations based on class
        Constitution = 50;
        Strength = 50;
        Dexterity = 50;
        Quickness = 50;
        Intelligence = 50;
        Piety = 50;
        Empathy = 50;
        Charisma = 50;
    }

    private void EquipBot()
    {
        // TODO: Implement equipment system for bots
        // For now, just ensure bot has basic gear
    }

    private ushort GetClassModel(string className)
    {
        // TODO: Return appropriate model based on class and realm
        // For now, return a basic model
        return Owner?.Model ?? 32; // Default male model
    }

    /// <summary>
    /// Save bot data to database
    /// </summary>
    public void SaveToDatabase()
    {
        BotDatabase.SaveBot(this);
    }

    /// <summary>
    /// Load bot data from database
    /// </summary>
    public void LoadFromDatabase()
    {
        // Bot data is loaded through BotDatabase.LoadBot()
        log.Debug($"LoadFromDatabase called for bot {Name}");
    }

    /// <summary>
    /// Stop following current target
    /// </summary>
    public void StopFollowing()
    {
        StopMoving();
        FollowTarget = null;
    }

    /// <summary>
    /// Move to a specific position
    /// </summary>
    public void MoveTo(Vector3 position)
    {
        WalkTo(position, MaxSpeed);
    }
}
}