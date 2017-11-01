using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;

namespace RAE.Game
{
    public abstract class Door : Spot
    {
        public readonly Room Dest;

        [DefaultValue(true)]
        public bool Opened { get; set; }

        public Door(RAEGame game, Room room, string defaultName, Article defaultArticle, Room dest)
            : base(game, room, defaultName, defaultArticle)
        {
            Opened = true;
            Dest = dest;

            Verbs["enter"] = (l, t) =>
                {
                    if (Opened)
                        Player.Instance.Location = Dest;
                    else
                        RAEGame.PrintLine(this + " is closed.");
                };
            Verbs["look"] = (l, t) =>
                {
                    if (Opened)
                    {
                        RAEGame.PrintLine("You can see " + Dest + " through the door:");
                        Game.TryLook(Dest, new string[0]);
                    }
                    else
                    {
                        RAEGame.PrintLine(this + " is closed.");
                    }
                };
        }

        public override void Init()
        {
        }
    }

    public class MultiItemDummy<P> : MultiItem where P : Item
    {
        public readonly P PrototypeInstance;

        private MultiItemDummy(P proto)
            : base(proto.Game, proto.DefaultName, proto.DefaultArticle)
        {
            PrototypeInstance = proto;
        }

        public static MultiItemDummy<P> GetDummy(int amount)
        {
            var d = new MultiItemDummy<P>(
                (P)typeof(P).GetProperty("Instance")
                .GetGetMethod().Invoke(null, null))
            {
                Amount = amount
            };
            return d;
        }

        public override void ResetInstance()
        {
            // this is not actually a singleton,
            // and shouldn't get wiped this way anyway!
            throw new NotImplementedException();
        }

        public override void Init()
        {
        }
    }

    public abstract class MultiItem : Item
    {
        public State SingleState { get; set; }
        public State PluralState { get; set; }

        private int mAmount;
        [DefaultValue(1)]
        public int Amount
        {
            get { return mAmount; }
            set
            {
                if (value == 1)
                {
                    CurrentState = SingleState;
                }
                else
                {
                    CurrentState = PluralState;
                }

                mAmount = value;
            }
        }

        public MultiItem(RAEGame game, string defaultName, Article defaultArticle)
            : base(game, defaultName, defaultArticle)
        {
            SingleState = Default;
            PluralState = Default;
        }

        public override string ToString()
        {
            if (Amount == 1)
                return base.ToString();
            else
                return RAEGame.Bold(Amount.ToString() + " " + Name);
        }
    }
}
