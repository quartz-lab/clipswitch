using UnityEngine;

namespace QuartzLab.ClipSwitch
{
    internal static class ClipSwitchLocalization
    {
        public static bool IsRussian
        {
            get
            {
                ClipSwitchLanguage language = ClipSwitchState.instance.Language;
                return language == ClipSwitchLanguage.Russian ||
                       (language == ClipSwitchLanguage.Auto && Application.systemLanguage == SystemLanguage.Russian);
            }
        }

        public static string T(string english, string russian)
        {
            return IsRussian ? russian : english;
        }

        public static GUIContent C(string english, string russian, string englishTooltip, string russianTooltip)
        {
            return new GUIContent(T(english, russian), T(englishTooltip, russianTooltip));
        }

        public static GUIContent C(string english, string russian)
        {
            return new GUIContent(T(english, russian));
        }

        public static string Operation(string value)
        {
            if (!IsRussian || string.IsNullOrEmpty(value))
                return value;
            switch (value)
            {
                case "Replace": return "Замена";
                case "Swap": return "Обмен";
                case "Process": return "Обработка";
                case "Restore": return "Восстановление";
                case "Batch replace": return "Пакетная замена";
                default: return value;
            }
        }
    }
}
