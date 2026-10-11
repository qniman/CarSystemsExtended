using System.Collections.Generic;
using CSE.Core;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Shop
{
    public class CatalogEntry
    {
        public string Id;
        public string Title;
        public string Blurb;
        public int Price;
        public PartDef[] Contents;
    }

    // Mail order the 1995 way: read the brochure, fill in the form, drop the envelope in the
    // post box at Teimo's, collect the parcel at the post office counter the next day and pay
    // cash on delivery (postiennakko).
    public static class MailOrder
    {
        public const int Shipping = 35;
        const float PostBoxReach = 1.5f;

        public static readonly CatalogEntry[] Entries =
        {
            new CatalogEntry
            {
                Id = "alarm",
                Title = "VARTIJA 95 car alarm with remote start",
                Blurb = "Alarm unit, siren, remote control, 2 x 15A fuse. Shock sensor, door, bonnet and " +
                        "ignition guard. Warms up your engine on cold mornings at the press of a button!",
                Price = 1890,
                Contents = PartCatalog.Kits.Alarm,
            },
            new CatalogEntry
            {
                Id = "locks",
                Title = "Central locking actuators (pair)",
                Blurb = "Electric door lock actuators for two doors. Operated by the VARTIJA 95 unit.",
                Price = 640,
                Contents = PartCatalog.Kits.DoorLocks,
            },
            new CatalogEntry
            {
                Id = "choke",
                Title = "Automatic choke servo",
                Blurb = "Pulls the choke for remote starts and lets it off as the engine warms. " +
                        "For carburettor cars. Requires VARTIJA 95.",
                Price = 420,
                Contents = PartCatalog.Kits.ChokeServo,
            },
        };

        public class Order
        {
            public string Kits; // comma separated entry ids
            public int Price;
            public int DaysLeft;
        }

        public static readonly List<Order> Pending = new List<Order>();
        public static bool BrochureGiven;

        static FsmInt globalDay;
        static FsmFloat money;
        static int lastDay;
        static Transform postBox, postOffice;

        public static void Init()
        {
            Pending.Clear();
            BrochureGiven = false;

            PartCatalog.Brochure.OnLooseHover = BrochureHover;
            PartCatalog.Envelope.OnHeld = EnvelopeHeld;
            PartCatalog.Parcel.OnLooseHover = ParcelHover;

            globalDay = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
            money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");

            // Store objects get switched off by LOD when far away; Transform.Find still sees them.
            var store = GameObject.Find("STORE");
            postBox = store != null ? store.transform.Find("LOD/Post Box") : null;
            // The spot where the game sells its own mail-order parcels; fall back to the office itself.
            postOffice = store != null
                ? store.transform.Find("LOD/ActivateStore/PostOffice/PostOrderBuy") ?? store.transform.Find("LOD/ActivateStore/PostOffice")
                : null;
            if (postBox == null || postOffice == null)
                ModConsole.Error("[CSE] Post box / post office not found, mail order will not work");
        }

        /// After the save is loaded: hand out the brochure once and start counting days.
        public static void AfterLoad()
        {
            lastDay = globalDay != null ? globalDay.Value : 0;
            if (!BrochureGiven)
            {
                SpawnBrochure();
                BrochureGiven = true;
            }
        }

        static void SpawnBrochure()
        {
            // Next to the game's own parts magazine, which lives in the house.
            Vector3 pos;
            var magazine = GameObject.Find("ITEMS/parts magazine(itemx)");
            if (magazine != null) pos = magazine.transform.position + new Vector3(0.3f, 0.1f, 0f);
            else
            {
                var cam = GameRefs.Camera.transform;
                pos = cam.position + cam.forward * 0.8f;
            }
            PartManager.Spawn(PartCatalog.Brochure, pos, Quaternion.identity);
            ModConsole.Print("[CSE] Autotarvike catalog delivered next to the parts magazine");
        }

        public static CatalogEntry Find(string id)
        {
            foreach (var e in Entries) if (e.Id == id) return e;
            return null;
        }

        // ---------------------------------------------------------------- brochure

        static void BrochureHover(Part p)
        {
            Gui.Interaction("READ CATALOG (RMB)");
            if (Input.GetMouseButtonDown(1)) CatalogUI.Open();
        }

        /// From the catalog form: an envelope appears in front of the player.
        public static void FillOrderForm(List<CatalogEntry> picks)
        {
            if (picks.Count == 0) return;
            var ids = new List<string>();
            int total = Shipping;
            foreach (var e in picks)
            {
                ids.Add(e.Id);
                total += e.Price;
            }
            var cam = GameRefs.Camera.transform;
            var env = PartManager.Spawn(PartCatalog.Envelope, cam.position + cam.forward * 0.5f, cam.rotation);
            env.Data = string.Join(",", ids.ToArray()) + ";" + total;
            Sfx.Game("HouseFoley", "mail_envelope_close", env.transform.position);
        }

        // ---------------------------------------------------------------- envelope

        static bool EnvelopeHeld(Part env)
        {
            if (postBox == null) return true;
            if (Vector3.Distance(env.transform.position, postBox.position) > PostBoxReach)
            {
                Gui.Interaction("ORDER: POST BOX AT TEIMO'S SHOP");
                return true;
            }

            Gui.Interaction("POST ORDER");
            if (Input.GetMouseButtonDown(0))
            {
                var order = ParseEnvelope(env.Data);
                if (order != null)
                {
                    Pending.Add(order);
                    ModConsole.Print("[CSE] Order posted: " + order.Kits + ", " + order.Price + " mk on delivery");
                }
                Sfx.Game("HouseFoley", "mail_insert", postBox.position);
                PartManager.Remove(env);
            }
            return true;
        }

        static Order ParseEnvelope(string data)
        {
            if (string.IsNullOrEmpty(data)) return null;
            var parts = data.Split(';');
            int price;
            if (parts.Length != 2 || !int.TryParse(parts[1], out price)) return null;
            return new Order { Kits = parts[0], Price = price, DaysLeft = 1 };
        }

        // ---------------------------------------------------------------- delivery

        public static void Tick()
        {
            if (globalDay == null || globalDay.Value == lastDay) return;
            lastDay = globalDay.Value;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (--Pending[i].DaysLeft > 0) continue;
                Deliver(Pending[i]);
                Pending.RemoveAt(i);
            }
        }

        public static void DeliverAllNow()
        {
            foreach (var o in Pending) Deliver(o);
            Pending.Clear();
        }

        static void Deliver(Order o)
        {
            if (postOffice == null) return;
            // Line parcels up next to each other on the post office side of the shop.
            int waiting = 0;
            foreach (var p in PartManager.Parts)
                if (p != null && p.Def == PartCatalog.Parcel && p.Price > 0) waiting++;
            Vector3 pos = postOffice.position + Vector3.up * 0.3f + postOffice.right * (0.45f * waiting);

            var parcel = PartManager.Spawn(PartCatalog.Parcel, pos, postOffice.rotation);
            parcel.Data = o.Kits;
            parcel.SetForSale(o.Price);
            ModConsole.Print("[CSE] Parcel waiting at the post office: " + o.Kits + ", " + o.Price + " mk");
        }

        // ---------------------------------------------------------------- parcel

        static void ParcelHover(Part parcel)
        {
            if (parcel.Price > 0)
            {
                Gui.Buy();
                Gui.Interaction("PARCEL, CASH ON DELIVERY " + parcel.Price + " MK");
                if (!Input.GetMouseButtonDown(0)) return;
                if (money == null || money.Value < parcel.Price)
                {
                    Sfx.Game("CarFoley", "dash_button", parcel.transform.position);
                    ModConsole.Print("[CSE] Not enough money for the parcel");
                    return;
                }
                money.Value -= parcel.Price;
                parcel.SetForSale(0);
                Sfx.Game("HouseFoley", "mail_envelope_open", parcel.transform.position);
                return;
            }

            Gui.Interaction("OPEN PARCEL (RMB)");
            if (!Input.GetMouseButtonDown(1)) return;

            Vector3 origin = parcel.transform.position + Vector3.up * 0.15f;
            int n = 0;
            foreach (var id in (parcel.Data ?? "").Split(','))
            {
                var entry = Find(id);
                if (entry == null) continue;
                foreach (var def in entry.Contents)
                {
                    // small spiral so the parts don't spawn inside each other
                    float a = n * 1.3f;
                    var offset = new Vector3(Mathf.Cos(a), 0.05f * n, Mathf.Sin(a)) * (0.12f + 0.03f * n);
                    PartManager.Spawn(def, origin + offset, Quaternion.identity);
                    n++;
                }
            }
            Sfx.Disassemble(parcel.transform.position);
            PartManager.Remove(parcel);
        }
    }
}
