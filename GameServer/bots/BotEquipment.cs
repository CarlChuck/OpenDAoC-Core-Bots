using System;
using System.Collections.Generic;
using System.Reflection;
using DOL.Database;
using log4net;

namespace DOL.GS
{
    public static class BotEquipment
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static void SetWeaponROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType, eInventorySlot slot, eDamageType damageType)
        {
            DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot, damageType);
            GameInventoryItem item = GameInventoryItem.Create(itemToCreate);
            living.Inventory.AddItem(slot, item);
        }

        public static void SetArmorROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType)
        {
            for (int i = Slot.HELM; i <= Slot.ARMS; i++)
            {
                if (i == Slot.JEWELRY || i == Slot.CLOAK)
                    continue;

                eInventorySlot slot = (eInventorySlot)i;
                DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot);
                GameInventoryItem item = GameInventoryItem.Create(itemToCreate);
                living.Inventory.AddItem(slot, item);
            }
        }

        public static void SetJewelryROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType)
        {
            for (int i = Slot.JEWELRY; i <= Slot.RIGHTRING; i++)
            {
                if (i is Slot.TORSO or Slot.LEGS or Slot.ARMS or Slot.FOREARMS or Slot.SHIELD)
                    continue;

                eInventorySlot slot = (eInventorySlot)i;
                DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot);
                GameInventoryItem item = GameInventoryItem.Create(itemToCreate);

                if (i == Slot.RIGHTRING || i == Slot.LEFTRING)
                    living.Inventory.AddItem(living.Inventory.FindFirstEmptySlot(eInventorySlot.LeftRing, eInventorySlot.RightRing), item);
                else if (i == Slot.LEFTWRIST || i == Slot.RIGHTWRIST)
                    living.Inventory.AddItem(living.Inventory.FindFirstEmptySlot(eInventorySlot.LeftBracer, eInventorySlot.RightBracer), item);
                else
                    living.Inventory.AddItem(slot, item);
            }
        }

        public static void SetInstrumentROG(GameLiving living, eRealm realm, eCharacterClass charClass, byte level, eObjectType objectType, eInventorySlot slot, eInstrumentType instrumentType)
        {
            DbItemTemplate itemToCreate = new GeneratedUniqueItem(false, realm, charClass, level, objectType, slot);
            itemToCreate.DPS_AF = (int)instrumentType;
            GameInventoryItem item = GameInventoryItem.Create(itemToCreate);
            living.Inventory.AddItem(slot, item);
        }

        public static void SetMeleeWeapon(IGamePlayer player, eObjectType weapType, eHand hand, eWeaponDamageType damageType = 0)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 4);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));

            if (itemList.Count != 0)
            {
                List<DbItemTemplate> itemsToKeep = new List<DbItemTemplate>();

                foreach (DbItemTemplate item in itemList)
                {
                    bool shouldAddItem = false;

                    switch (hand)
                    {
                        case eHand.oneHand:
                            shouldAddItem = item.Item_Type == Slot.RIGHTHAND || item.Item_Type == Slot.LEFTHAND;
                            break;
                        case eHand.leftHand:
                            shouldAddItem = item.Item_Type == Slot.LEFTHAND;
                            break;
                        case eHand.twoHand:
                            shouldAddItem = item.Item_Type == Slot.TWOHAND && (damageType == 0 || item.Type_Damage == (int)damageType);
                            break;
                    }

                    if (shouldAddItem)
                        itemsToKeep.Add(item);
                }

                if (itemsToKeep.Count != 0)
                {
                    DbItemTemplate itemTemplate = itemsToKeep[Util.Random(itemsToKeep.Count - 1)];
                    AddItem(player, itemTemplate, hand);
                }
            }
            else
                log.Info("No melee weapon found for " + player.Name);
        }

        public static void SetRangedWeapon(IGamePlayer player, eObjectType weapType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("Item_Type").IsEqualTo(13).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Count != 0)
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                AddItem(player, itemTemplate);
            }
            else
                log.Info("No ranged weapon found for " + player.Name);
        }

        public static void SetShield(IGamePlayer player, int shieldSize)
        {
            if (shieldSize < 1)
                return;

            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)eObjectType.Shield).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("Type_Damage").IsEqualTo(shieldSize).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Count != 0)
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                AddItem(player, itemTemplate);
            }
            else
                log.Info("No shield found for " + player.Name);
        }

        public static void SetArmor(IGamePlayer player, eObjectType armorType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)armorType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));

            if (itemList.Count != 0)
            {
                Dictionary<int, List<DbItemTemplate>> armorSlots = new Dictionary<int, List<DbItemTemplate>>();

                foreach (DbItemTemplate template in itemList)
                {
                    if (!armorSlots.TryGetValue(template.Item_Type, out List<DbItemTemplate> slotList))
                    {
                        slotList = new List<DbItemTemplate>();
                        armorSlots[template.Item_Type] = slotList;
                    }

                    slotList.Add(template);
                }

                foreach (var pair in armorSlots)
                {
                    if (pair.Value.Count != 0)
                    {
                        DbItemTemplate itemTemplate = pair.Value[Util.Random(pair.Value.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }
            }
            else
                log.Info("No armor found for " + player.Name);
        }

        public static void SetInstrument(IGamePlayer player, eObjectType weapType, eInventorySlot slot, eInstrumentType instrumentType)
        {
            int min = Math.Max(1, player.Level - 6);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)weapType).And(
                                                                       DB.Column("DPS_AF").IsEqualTo((int)instrumentType).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1))))));

            if (itemList.Count != 0)
            {
                DbItemTemplate itemTemplate = itemList[Util.Random(itemList.Count - 1)];
                DbInventoryItem item = GameInventoryItem.Create(itemTemplate);
                player.Inventory.AddItem(slot, item);
            }
            else
                log.Info("No instrument found for " + player.Name);
        }

        public static void SetJewelry(IGamePlayer player)
        {
            int min = Math.Max(1, player.Level - 30);
            int max = Math.Min(51, player.Level + 3);

            IList<DbItemTemplate> itemList;
            List<DbItemTemplate> cloakList = new List<DbItemTemplate>();
            List<DbItemTemplate> jewelryList = new List<DbItemTemplate>();
            List<DbItemTemplate> ringList = new List<DbItemTemplate>();
            List<DbItemTemplate> wristList = new List<DbItemTemplate>();
            List<DbItemTemplate> neckList = new List<DbItemTemplate>();
            List<DbItemTemplate> waistList = new List<DbItemTemplate>();

            itemList = GameServer.Database.SelectObjects<DbItemTemplate>(DB.Column("Level").IsGreaterOrEqualTo(min).And(
                                                                       DB.Column("Level").IsLessOrEqualTo(max).And(
                                                                       DB.Column("Object_Type").IsEqualTo((int)eObjectType.Magical).And(
                                                                       DB.Column("Realm").IsEqualTo((int)player.Realm)).And(
                                                                       DB.Column("IsPickable").IsEqualTo(1)))));

            if (itemList.Count != 0)
            {
                foreach (DbItemTemplate template in itemList)
                {
                    if (template.Item_Type == Slot.CLOAK)
                    {
                        template.Color = Util.Random((Enum.GetValues(typeof(eColor)).Length));
                        cloakList.Add(template);
                    }
                    else if (template.Item_Type == Slot.JEWELRY)
                        jewelryList.Add(template);
                    else if (template.Item_Type == Slot.LEFTRING || template.Item_Type == Slot.RIGHTRING)
                        ringList.Add(template);
                    else if (template.Item_Type == Slot.LEFTWRIST || template.Item_Type == Slot.RIGHTWRIST)
                        wristList.Add(template);
                    else if (template.Item_Type == Slot.NECK)
                        neckList.Add(template);
                    else if (template.Item_Type == Slot.WAIST)
                        waistList.Add(template);
                }

                List<List<DbItemTemplate>> masterList = new List<List<DbItemTemplate>>
                {
                    cloakList,
                    jewelryList,
                    neckList,
                    waistList
                };

                foreach (List<DbItemTemplate> list in masterList)
                {
                    if (list.Count != 0)
                    {
                        DbItemTemplate itemTemplate = list[Util.Random(list.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }

                for (int i = 0; i < 2; i++)
                {
                    if (ringList.Count != 0)
                    {
                        DbItemTemplate itemTemplate = ringList[Util.Random(ringList.Count - 1)];
                        AddItem(player, itemTemplate);
                    }

                    if (wristList.Count != 0)
                    {
                        DbItemTemplate itemTemplate = wristList[Util.Random(wristList.Count - 1)];
                        AddItem(player, itemTemplate);
                    }
                }

                if (player.Inventory.GetItem(eInventorySlot.Cloak) == null)
                {
                    DbItemTemplate cloak = GameServer.Database.FindObjectByKey<DbItemTemplate>("cloak");

                    if (cloak != null)
                    {
                        cloak.Color = Util.Random((Enum.GetValues(typeof(eColor)).Length));
                        AddItem(player, cloak);
                    }
                }
            }
            else
                log.Info("No jewelry of any kind found for " + player.Name);
        }

        private static void AddItem(IGamePlayer player, DbItemTemplate itemTemplate, eHand hand = eHand.None)
        {
            if (itemTemplate == null)
            {
                log.Info("itemTemplate in AddItem is null");
                return;
            }

            DbInventoryItem item = GameInventoryItem.Create(itemTemplate);

            if (item != null)
            {
                if (item.Item_Type == Slot.LEFTRING || item.Item_Type == Slot.RIGHTRING)
                {
                    player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.LeftRing, eInventorySlot.RightRing), item);
                    return;
                }
                else if (item.Item_Type == Slot.LEFTWRIST || item.Item_Type == Slot.RIGHTWRIST)
                {
                    player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.LeftBracer, eInventorySlot.RightBracer), item);
                    return;
                }
                else if (item.Item_Type == Slot.LEFTHAND && item.Object_Type != (int)eObjectType.Shield && hand == eHand.oneHand)
                {
                    player.Inventory.AddItem(eInventorySlot.RightHandWeapon, item);
                    return;
                }
                else
                {
                    if (item.Object_Type == (int)eObjectType.Shield &&
                        (player.CharacterClass.ID == (int)eCharacterClass.Infiltrator ||
                        player.CharacterClass.ID == (int)eCharacterClass.Mercenary ||
                        player.CharacterClass.ID == (int)eCharacterClass.Nightshade ||
                        player.CharacterClass.ID == (int)eCharacterClass.Ranger ||
                        player.CharacterClass.ID == (int)eCharacterClass.Blademaster ||
                        player.CharacterClass.ID == (int)eCharacterClass.Shadowblade ||
                        player.CharacterClass.ID == (int)eCharacterClass.Berserker ||
                        player.CharacterClass.ID == (int)eCharacterClass.Savage))
                    {
                        player.Inventory.AddItem(player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), item);
                    }
                    else
                        player.Inventory.AddItem((eInventorySlot)item.Item_Type, item);
                }
            }
            else
                log.Info("Item failed to be created for " + player.Name);
        }
    }
}
