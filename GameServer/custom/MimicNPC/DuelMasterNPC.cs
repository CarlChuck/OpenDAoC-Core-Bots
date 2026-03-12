using DOL.Database;
using DOL.GS;
using DOL.GS.API;
using DOL.GS.PacketHandler;
using DOL.GS.Scripts;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DOL.GS.Scripts
{
    public class DuelMasterNPC : GameNPC
    {
        private ECSGameTimer m_duelControlTimer;
        private int m_duelInterval;
        private bool m_isDuelRunning;

        private Point3D m_spawnPositionOne;
        private Point3D m_spawnPositionTwo;

        private MimicNPC m_mimicOne;
        private MimicNPC m_mimicTwo;

        public override eGameObjectType GameObjectType => eGameObjectType.NPC;

        public override bool Interact(GamePlayer player)
        {
            if (!base.Interact(player))
                return false;

            player.Out.SendMessage("[Duel] [Watch]", eChatType.CT_Say, eChatLoc.CL_PopupWindow);
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string str)
        {
            if (!base.WhisperReceive(source, str))
                return false;

            GamePlayer player = source as GamePlayer;

            if (player == null)
                return false;

            switch (str)
            {
                case "Duel":
                {
                    if (player.Duel != null)
                    {
                        string message = "You are already in a duel.";
                        player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_PopupWindow);
                    }
                    else if (m_isDuelRunning)
                    {
                        string message = "A duel is in progress, please wait for it to finish.";
                        player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_PopupWindow);
                    }
                    else
                    {
                        MimicNPC mimic = MimicManager.GetMimic(MimicManager.GetRandomMimicClass(player.Realm), player.Level);
                        int xPos = X + Util.Random(-500, 500);
                        int yPos = Y + Util.Random(-500, 500);
                        int zPos = Z;

                        MimicManager.AddMimicToWorld(mimic, new Point3D(xPos, yPos, zPos), CurrentRegionID);
                        mimic.Duel.Start();
                    }
                    break;
                }

                case "Watch":
                {
                    if (m_isDuelRunning)
                    {
                        string message = "A duel is in progress, please wait for it to finish.";
                        player.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_PopupWindow);
                    }
                    else
                    {
                        int xPos = X + Util.Random(-250, 250);
                        int yPos = Y + Util.Random(-250, 250);
                        int zPos = Z;

                        m_spawnPositionOne = new Point3D(xPos, yPos, zPos);
                        m_spawnPositionTwo = new Point3D(xPos + 2000, yPos, zPos);

                        m_mimicOne = MimicManager.GetMimic(MimicManager.GetRandomMeleeClass(), 50);
                        m_mimicTwo = MimicManager.GetMimic(MimicManager.GetRandomMeleeClass(), 50);

                        MimicManager.AddMimicToWorld(m_mimicOne, m_spawnPositionOne, CurrentRegionID);
                        MimicManager.AddMimicToWorld(m_mimicTwo, m_spawnPositionTwo, CurrentRegionID);

                        m_mimicOne.MimicBrain.FSM.SetCurrentState(eFSMStateType.DUEL);
                        m_mimicTwo.MimicBrain.FSM.SetCurrentState(eFSMStateType.DUEL);

                        m_duelControlTimer = new ECSGameTimer(this, new ECSGameTimer.ECSTimerCallback(StartDuelTimerCallback), 5000);
                    }

                    break;
                }
            }

            return true;
        }

        private int StartDuelTimerCallback(ECSGameTimer timer)
        {
            if (!m_isDuelRunning && m_mimicOne != null && m_mimicOne.IsDuelReady
                                 && m_mimicTwo != null && m_mimicTwo.IsDuelReady)
            {
                m_isDuelRunning = true;

                m_mimicOne.Health = m_mimicOne.MaxHealth;
                m_mimicOne.Mana = m_mimicOne.MaxMana;
                m_mimicOne.Endurance = m_mimicOne.MaxEndurance;

                m_mimicTwo.Health = m_mimicTwo.MaxHealth;
                m_mimicTwo.Mana = m_mimicTwo.MaxMana;
                m_mimicTwo.Endurance = m_mimicTwo.MaxEndurance;

                m_mimicOne.MimicBrain.AddToAggroList(m_mimicTwo, 100);
                m_mimicTwo.MimicBrain.AddToAggroList(m_mimicOne, 100);
                m_mimicOne.TargetObject = m_mimicTwo;
                m_mimicTwo.TargetObject = m_mimicOne;

                GameDuel duel = new(m_mimicOne, m_mimicTwo);
                duel.Start();

                //m_mimicOne.MimicBrain.FSM.SetCurrentState(eFSMStateType.WAKING_UP);
                //m_mimicTwo.MimicBrain.FSM.SetCurrentState(eFSMStateType.WAKING_UP);
            }

            if (!(m_mimicOne.ObjectState == eObjectState.Active) || !(m_mimicTwo.ObjectState == eObjectState.Active))
            {
                ProgressDuel();
            }

            return 5000;
        }

        private void ProgressDuel()
        {
            m_isDuelRunning = false;

            m_mimicOne = m_mimicOne.ObjectState == eObjectState.Active ? m_mimicOne : m_mimicTwo;
            m_mimicOne.Health = m_mimicOne.MaxHealth;
            m_mimicOne.Mana = m_mimicOne.MaxMana;
            m_mimicOne.Endurance = m_mimicOne.MaxEndurance;

            foreach (Skill skill in m_mimicOne.GetAllDisabledSkills())
                m_mimicOne.RemoveDisabledSkill(skill);

            m_mimicOne.MoveTo(CurrentRegionID, m_spawnPositionOne.X, m_spawnPositionOne.Y, m_spawnPositionOne.Z, 0);

            m_mimicTwo = MimicManager.GetMimic(MimicManager.GetRandomMeleeClass(), 50);
            MimicManager.AddMimicToWorld(m_mimicTwo, m_spawnPositionTwo, CurrentRegionID);

            m_mimicOne.MimicBrain.FSM.SetCurrentState(eFSMStateType.DUEL);
            m_mimicTwo.MimicBrain.FSM.SetCurrentState(eFSMStateType.DUEL);

            //m_duelControlTimer = new ECSGameTimer(this, new ECSGameTimer.ECSTimerCallback(StartDuelTimerCallback), 5000);
        }

        public DuelMasterNPC()
        { }

        public override bool AddToWorld()
        {
            Realm = eRealm.Albion;

            GameNpcInventoryTemplate template = new GameNpcInventoryTemplate();

            switch (Realm)
            {
                case eRealm.Albion:
                Name = "Albion Duel Master";
                Model = 39;
                template.AddNPCEquipment(eInventorySlot.HeadArmor, 1290);
                template.AddNPCEquipment(eInventorySlot.TorsoArmor, 713);
                template.AddNPCEquipment(eInventorySlot.ArmsArmor, 715);
                template.AddNPCEquipment(eInventorySlot.LegsArmor, 714);
                template.AddNPCEquipment(eInventorySlot.HandsArmor, 716);
                template.AddNPCEquipment(eInventorySlot.FeetArmor, 717);
                template.AddNPCEquipment(eInventorySlot.Cloak, 4105);
                break;

                case eRealm.Midgard:
                Name = "Midgard Duel Master";
                Model = 153;
                template.AddNPCEquipment(eInventorySlot.HeadArmor, 1291);
                template.AddNPCEquipment(eInventorySlot.TorsoArmor, 698);
                template.AddNPCEquipment(eInventorySlot.ArmsArmor, 700);
                template.AddNPCEquipment(eInventorySlot.LegsArmor, 699);
                template.AddNPCEquipment(eInventorySlot.HandsArmor, 701);
                template.AddNPCEquipment(eInventorySlot.FeetArmor, 702);
                template.AddNPCEquipment(eInventorySlot.Cloak, 4107);
                break;

                case eRealm.Hibernia:
                Name = "Hibernia Duel Master";
                Model = 302;
                Size = 55;
                template.AddNPCEquipment(eInventorySlot.HeadArmor, 1292);
                template.AddNPCEquipment(eInventorySlot.TorsoArmor, 739);
                template.AddNPCEquipment(eInventorySlot.ArmsArmor, 741);
                template.AddNPCEquipment(eInventorySlot.LegsArmor, 740);
                template.AddNPCEquipment(eInventorySlot.HandsArmor, 742);
                template.AddNPCEquipment(eInventorySlot.FeetArmor, 743);
                template.AddNPCEquipment(eInventorySlot.Cloak, 4109);
                break;
            }

            Inventory = template.CloseTemplate();
            Flags |= eFlags.PEACE;
            Level = 75;

            return base.AddToWorld();
        }

        public override bool RemoveFromWorld()
        {
            if (!base.RemoveFromWorld())
                return false;

            if (m_mimicOne != null)
                m_mimicOne.Delete();

            if (m_mimicTwo != null)
                m_mimicTwo.Delete();

            return true;
        }
    }

    public static class DuelManager
    {
        public class MimicDuel
        {
        }
    }
}