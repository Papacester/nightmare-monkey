using MelonLoader;

namespace Narcopelago
{
    internal static class NarcopelagoConfig
    {
        private const string CATEGORY_NAME = "Narcopelago";
        public static MelonPreferences_Category Category { get; private set; }
        public static MelonPreferences_Entry<string> Host { get; private set; }
        public static MelonPreferences_Entry<int> Port { get; private set; }
        public static MelonPreferences_Entry<string> SlotName { get; private set; }
        public static MelonPreferences_Entry<string> Password { get; private set; }

        public static void Initalize()
        {
            Category = MelonPreferences.CreateCategory(CATEGORY_NAME);
            Category.SetFilePath("UserData/Narcopelago.cfg", true, false);
            Host = Category.CreateEntry<string>("Host", "");
            Port = Category.CreateEntry<int>("Port", -1);
            SlotName = Category.CreateEntry<string>("Category", "");
            Password = Category.CreateEntry<string>("Password", "");
            Category.SaveToFile(false);
        }

        public static void Save(string host, int port, string slotName, string password)
        {
            Host.Value = host;
            Port.Value = port;
            SlotName.Value = slotName;
            Password.Value = password;
            Category.SaveToFile(false);
        }

        public static (string host, int port, string slotName, string password) Load()
        {
            return (Host.Value, Port.Value, SlotName.Value, Password.Value);
        }
    }
}
