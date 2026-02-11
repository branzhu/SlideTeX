// SlideTeX Note: Localization resolver that selects resource strings for the active culture.

using System;
using System.Globalization;
using System.Threading;
using SlideTeX.VstoAddin.Properties;

namespace SlideTeX.VstoAddin.Localization
{
    /// <summary>
    /// Centralized localization accessor that resolves Office UI culture with fallback behavior.
    /// </summary>
    internal static class LocalizationManager
    {
        private static readonly object SyncRoot = new object();
        private static readonly CultureInfo FallbackCulture = CultureInfo.GetCultureInfo("en-US");
        private static CultureInfo _uiCulture = FallbackCulture;
        private static bool _isInitialized;

        public static CultureInfo UICulture
        {
            get { return _uiCulture; }
        }

        public static string UICultureName
        {
            get { return _uiCulture.Name; }
        }

        /// <summary>
        /// Resolves and applies the UI culture once during add-in startup.
        /// </summary>
        public static void Initialize(ThisAddIn addIn)
        {
            lock (SyncRoot)
            {
                if (_isInitialized)
                {
                    return;
                }

                var resolved = ResolveOfficeUiCultureName(addIn);
                _uiCulture = NormalizeSupportedCulture(resolved);

                Resources.Culture = _uiCulture;
                Thread.CurrentThread.CurrentUICulture = _uiCulture;
                _isInitialized = true;
            }
        }

        /// <summary>
        /// Retrieves a localized resource string by key with fallback to English and key echo.
        /// </summary>
        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var value = Resources.ResourceManager.GetString(key, _uiCulture);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            value = Resources.ResourceManager.GetString(key, FallbackCulture);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return key;
        }

        /// <summary>
        /// Formats a localized resource template with current UI culture semantics.
        /// </summary>
        public static string Format(string key, params object[] args)
        {
            var template = Get(key);
            return string.Format(_uiCulture, template, args ?? Array.Empty<object>());
        }

        private static CultureInfo NormalizeSupportedCulture(string cultureName)
        {
            if (!string.IsNullOrWhiteSpace(cultureName) &&
                cultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return CultureInfo.GetCultureInfo("zh-CN");
            }

            return FallbackCulture;
        }

        private static string ResolveOfficeUiCultureName(ThisAddIn addIn)
        {
            try
            {
                dynamic app = addIn != null ? addIn.Application : null;
                if (app != null)
                {
                    dynamic languageSettings = app.LanguageSettings;
                    if (languageSettings != null)
                    {
                        // msoLanguageIDUI = 2
                        int lcid = (int)languageSettings.LanguageID(2);
                        if (lcid > 0)
                        {
                            return CultureInfo.GetCultureInfo(lcid).Name;
                        }
                    }
                }
            }
            catch
            {
                // Ignore and fall back to thread UI culture.
            }

            return Thread.CurrentThread.CurrentUICulture.Name;
        }
    }
}


