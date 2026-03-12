using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DOL.GS;

namespace DOL.GS.Scripts
{
    class MimicNPCInventory : GameLivingInventory
    {
        public MimicNPCInventory()
        {
        }

        protected override eInventorySlot GetValidInventorySlot(eInventorySlot slot)
        {
            switch (slot)
            {
                case eInventorySlot.LastEmptyQuiver:
                slot = FindLastEmptySlot(eInventorySlot.FirstQuiver, eInventorySlot.FourthQuiver);
                break;
                case eInventorySlot.FirstEmptyQuiver:
                slot = FindFirstEmptySlot(eInventorySlot.FirstQuiver, eInventorySlot.FourthQuiver);
                break;
                case eInventorySlot.LastEmptyVault:
                slot = FindLastEmptySlot(eInventorySlot.FirstVault, eInventorySlot.LastVault);
                break;
                case eInventorySlot.FirstEmptyVault:
                slot = FindFirstEmptySlot(eInventorySlot.FirstVault, eInventorySlot.LastVault);
                break;
                case eInventorySlot.LastEmptyBackpack:
                slot = FindLastEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                break;
                case eInventorySlot.FirstEmptyBackpack:
                slot = FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
                break;
                case eInventorySlot.LastEmptyBagHorse:
                slot = FindLastEmptySlot(eInventorySlot.FirstBagHorse, eInventorySlot.LastBagHorse);
                break;
                case eInventorySlot.FirstEmptyBagHorse:
                slot = FindFirstEmptySlot(eInventorySlot.FirstBagHorse, eInventorySlot.LastBagHorse);
                break;
            }

            if ((slot >= eInventorySlot.FirstBackpack && slot <= eInventorySlot.LastBackpack)
                // || ( slot >= eInventorySlot.Mithril && slot <= eInventorySlot.Copper ) // can't place items in money slots, is it?
                || (slot >= eInventorySlot.HorseArmor && slot <= eInventorySlot.Horse)
                || (slot >= eInventorySlot.FirstVault && slot <= eInventorySlot.LastVault)
                || (slot >= eInventorySlot.HouseVault_First && slot <= eInventorySlot.HouseVault_Last)
                || (slot >= eInventorySlot.Consignment_First && slot <= eInventorySlot.Consignment_Last)
                || (slot == eInventorySlot.PlayerPaperDoll)
                || (slot == eInventorySlot.Mythical)

                || (slot >= eInventorySlot.FirstBagHorse && slot <= eInventorySlot.LastBagHorse))
                return slot;


            return base.GetValidInventorySlot(slot);
        }
    }
}