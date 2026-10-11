using System.Collections.Generic;
using CSE.Parts;

namespace CSE.Core
{
    // Player-facing text in English and Russian. Debug overlay and console logs stay English.
    public static class L
    {
        public enum Lang { English, Russian }

        public static Lang Current = Lang.English;

        public static readonly string[] LanguageNames = { "English", "Русский" };

        static readonly Dictionary<string, string[]> text = new Dictionary<string, string[]>
        {
            // ---- interaction hints (game HUD, upper case like the game's own)
            { "disconnect_wire", new[] { "DISCONNECT WIRE (RMB)", "ОТКЛЮЧИТЬ ПРОВОД (ПКМ)" } },
            { "disconnect_power", new[] { "DISCONNECT POWER (RMB)", "ОТКЛЮЧИТЬ ПИТАНИЕ (ПКМ)" } },
            { "unplug_first", new[] { "UNPLUG {0} FIRST", "СНАЧАЛА ОТКЛЮЧИТЕ: {0}" } },
            { "unlock_door", new[] { "UNLOCK DOOR", "ОТПЕРЕТЬ ДВЕРЬ" } },
            { "locked", new[] { "LOCKED", "ЗАПЕРТО" } },
            { "fob_attach", new[] { "PRESS {0} TO PUT ON SATSUMA KEYS", "НАЖМИТЕ {0}, ЧТОБЫ ПОВЕСИТЬ НА КЛЮЧИ САТСУМЫ" } },
            { "feed", new[] { "ALARM FEED (FUSE BOX)", "ПИТАНИЕ СИГНАЛИЗАЦИИ (ПРЕДОХРАНИТЕЛИ)" } },
            { "cancel_wire", new[] { "CANCEL WIRE", "ОТМЕНИТЬ ПРОВОД" } },
            { "read_catalog", new[] { "READ CATALOG (RMB)", "ЧИТАТЬ КАТАЛОГ (ПКМ)" } },
            { "post_where", new[] { "ORDER: POST BOX AT TEIMO'S SHOP", "ЗАКАЗ: ПОЧТОВЫЙ ЯЩИК У МАГАЗИНА ТЕЙМО" } },
            { "post_order", new[] { "POST ORDER", "ОТПРАВИТЬ ЗАКАЗ" } },
            { "parcel_cod", new[] { "PARCEL, CASH ON DELIVERY {0} MK", "ПОСЫЛКА, НАЛОЖЕННЫЙ ПЛАТЁЖ {0} МК" } },
            { "open_parcel", new[] { "OPEN PARCEL (RMB)", "ОТКРЫТЬ ПОСЫЛКУ (ПКМ)" } },
            { "hold_brake", new[] { "HOLD THE BRAKE PEDAL", "УДЕРЖИВАЙТЕ ПЕДАЛЬ ТОРМОЗА" } },

            // ---- part names (shown by the game when looking at a loose part)
            { "part_cse_alarm_unit", new[] { "alarm unit", "блок сигнализации" } },
            { "part_cse_siren", new[] { "alarm siren", "сирена сигнализации" } },
            { "part_cse_fuse", new[] { "fuse 15a", "предохранитель 15а" } },
            { "part_cse_status_led", new[] { "alarm led", "светодиод сигнализации" } },
            { "part_cse_fob", new[] { "alarm remote", "брелок сигнализации" } },
            { "part_cse_lock_actuator", new[] { "door lock actuator", "активатор замка двери" } },
            { "part_cse_choke_servo", new[] { "choke servo", "сервопривод подсоса" } },
            { "part_cse_brochure", new[] { "autotarvike catalog", "каталог autotarvike" } },
            { "part_cse_envelope", new[] { "order envelope", "конверт с заказом" } },
            { "part_cse_parcel", new[] { "parcel", "посылка" } },

            // ---- catalog page
            { "cat_sub", new[] { "Car accessories by mail  ·  Spring 1995  ·  Delivery to your post office",
                                 "Автотовары по почте  ·  Весна 1995  ·  Доставка на ваше почтовое отделение" } },
            { "cat_terms", new[] { "Postage and handling {0} mk. Payment cash on delivery (postiennakko) when you collect the parcel. " +
                                   "Post the order form in the post box at your local shop; parcels arrive the next day.",
                                   "Доставка {0} мк. Оплата наложенным платежом (postiennakko) при получении посылки. " +
                                   "Опустите бланк заказа в почтовый ящик у ближайшего магазина, посылка придёт на следующий день." } },
            { "cat_total", new[] { "TOTAL {0} mk", "ИТОГО {0} мк" } },
            { "cat_tick", new[] { "Tick the items you want", "Отметьте нужные товары" } },
            { "cat_fill", new[] { "FILL IN ORDER FORM", "ЗАПОЛНИТЬ БЛАНК" } },
            { "cat_close", new[] { "CLOSE", "ЗАКРЫТЬ" } },
            { "cat_price", new[] { "{0} mk", "{0} мк" } },

            { "kit_alarm_title", new[] { "VARTIJA 95 car alarm with remote start", "Сигнализация VARTIJA 95 с автозапуском" } },
            { "kit_alarm_blurb", new[] {
                "Alarm unit, siren, remote control, dash LED, 2 x 15A fuse. Shock sensor, door, bonnet and ignition guard. " +
                "Warms up your engine on cold mornings at the press of a button!",
                "Блок сигнализации, сирена, брелок, светодиод на торпеду, 2 предохранителя 15А. Датчик удара, охрана дверей, " +
                "капота и зажигания. Прогреет двигатель холодным утром нажатием одной кнопки!" } },
            { "kit_locks_title", new[] { "Central locking actuators (pair)", "Активаторы центрального замка (пара)" } },
            { "kit_locks_blurb", new[] {
                "Electric door lock actuators for two doors. Operated by the VARTIJA 95 unit.",
                "Электрические активаторы замков для двух дверей. Управляются блоком VARTIJA 95." } },
            { "kit_choke_title", new[] { "Automatic choke servo", "Автоматический сервопривод подсоса" } },
            { "kit_choke_blurb", new[] {
                "Pulls the choke for remote starts and lets it off as the engine warms. For carburettor cars. Requires VARTIJA 95.",
                "Вытягивает подсос при автозапуске и отпускает его по мере прогрева. Для карбюраторных машин. Требуется VARTIJA 95." } },
        };

        public static string T(string key)
        {
            string[] v;
            if (!text.TryGetValue(key, out v)) return key;
            int i = (int)Current;
            return i < v.Length && !string.IsNullOrEmpty(v[i]) ? v[i] : v[0];
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }

        public static string PartName(PartDef def)
        {
            return T("part_" + def.Id);
        }

        /// Part name for HUD labels (upper case, culture-proof for Cyrillic).
        public static string PartLabel(PartDef def)
        {
            return PartName(def).ToUpperInvariant();
        }
    }
}
