using System.Resources;

namespace System
{
    // The runtime libraries' generated SR class: with UseSystemResourceKeys the key is the message.
    internal static class SR
    {
        private static readonly bool s_usingResourceKeys =
            AppContext.TryGetSwitch("System.Resources.UseSystemResourceKeys", out bool usingResourceKeys) && usingResourceKeys;
        private static ResourceManager? s_resourceManager;

        internal static bool UsingResourceKeys() => s_usingResourceKeys;

        internal static ResourceManager ResourceManager => s_resourceManager ??= new ResourceManager(typeof(FxResources.IlResourceKeysLib.SR));

        internal static string GetResourceString(string resourceKey)
        {
            if (UsingResourceKeys()) return resourceKey;
            return ResourceManager.GetString(resourceKey) ?? resourceKey;
        }

        internal static string Format(string resourceFormat, object? p1) =>
            UsingResourceKeys() ? string.Join(", ", resourceFormat, p1) : string.Format(resourceFormat, p1);

        internal static string EmptyQueue => GetResourceString("EmptyQueue");
        internal static string TooMany => GetResourceString("TooMany");
    }
}

namespace FxResources.IlResourceKeysLib
{
    internal static class SR { }
}

namespace IlResourceKeysLib
{
    public static class Messages
    {
        public static string Empty() => SR.EmptyQueue;
        public static string TooMany(int count) => SR.Format(SR.TooMany, count);
        public static bool UsingKeys() => SR.UsingResourceKeys();
        public static void Throw() => throw new InvalidOperationException(SR.EmptyQueue);
    }
}
