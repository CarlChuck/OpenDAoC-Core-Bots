using DOL.GS;
using DOL.Network;
using DOL.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DOL.GS.Scripts
{
    public class DummyClient : GameClient
    {
        public DummyClient(BaseServer srvr) : base(srvr)
        {
            Account = new DbAccount();
            Account.Language = "EN";
            Account.PrivLevel = (int)ePrivLevel.Player;
        }
    }
}
