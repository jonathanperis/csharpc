// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// csharpc: DetectDefaultAppleLocaleName (pal_locale.m in dotnet/runtime, Objective-C) in C, over
// CoreFoundation's CFLocale (NSLocale is toll-free bridged to it: languageCode and countryCode are
// kCFLocaleLanguageCode / kCFLocaleCountryCode, localeIdentifier is CFLocaleGetIdentifier). The framework
// is loaded lazily, only when the ICU default locale is the POSIX one, so programs don't link it.
// See THIRD-PARTY-NOTICES.md.

#ifdef __APPLE__

#include <dlfcn.h>
#include <stdlib.h>
#include <string.h>

#include "pal_locale_internal.h"

typedef const void* CFTypeRef;
typedef const struct __CFString* CFStringRef;
typedef const struct __CFLocale* CFLocaleRef;
typedef long CFIndex;
typedef unsigned int CFStringEncoding;
#define kCFStringEncodingUTF8 0x08000100

typedef CFLocaleRef (*CFLocaleCopyCurrent_fn)(void);
typedef CFTypeRef (*CFLocaleGetValue_fn)(CFLocaleRef, CFStringRef);
typedef CFStringRef (*CFLocaleGetIdentifier_fn)(CFLocaleRef);
typedef CFIndex (*CFStringGetLength_fn)(CFStringRef);
typedef unsigned char (*CFStringGetCString_fn)(CFStringRef, char*, CFIndex, CFStringEncoding);
typedef void (*CFRelease_fn)(CFTypeRef);

// strdup (not declared by strict C11 headers everywhere).
static char* CopyString(const char* s)
{
    size_t length = strlen(s) + 1;
    char* copy = (char*)malloc(length);
    if (copy != NULL) memcpy(copy, s, length);
    return copy;
}

// A CFString as UTF-8 (empty for NULL / failures).
static void ToUtf8(CFStringGetCString_fn getCString, CFStringRef s, char* buffer, size_t size)
{
    buffer[0] = '\0';
    if (s == NULL || !getCString(s, buffer, (CFIndex)size, kCFStringEncodingUTF8))
        buffer[0] = '\0';
}

char* DetectDefaultAppleLocaleName(void)
{
    void* cf = dlopen("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", RTLD_LAZY);
    if (cf == NULL)
        return CopyString("");

    CFLocaleCopyCurrent_fn copyCurrent = (CFLocaleCopyCurrent_fn)dlsym(cf, "CFLocaleCopyCurrent");
    CFLocaleGetValue_fn getValue = (CFLocaleGetValue_fn)dlsym(cf, "CFLocaleGetValue");
    CFLocaleGetIdentifier_fn getIdentifier = (CFLocaleGetIdentifier_fn)dlsym(cf, "CFLocaleGetIdentifier");
    CFStringGetCString_fn getCString = (CFStringGetCString_fn)dlsym(cf, "CFStringGetCString");
    CFRelease_fn release = (CFRelease_fn)dlsym(cf, "CFRelease");
    CFStringRef* languageKey = (CFStringRef*)dlsym(cf, "kCFLocaleLanguageCode");
    CFStringRef* countryKey = (CFStringRef*)dlsym(cf, "kCFLocaleCountryCode");
    if (!copyCurrent || !getValue || !getIdentifier || !getCString || !release || !languageKey || !countryKey)
        return CopyString("");

    CFLocaleRef currentLocale = copyCurrent();
    if (currentLocale == NULL)
        return CopyString("");

    char language[64], country[64], identifier[ULOC_FULLNAME_CAPACITY];
    ToUtf8(getCString, (CFStringRef)getValue(currentLocale, *languageKey), language, sizeof(language));
    ToUtf8(getCString, (CFStringRef)getValue(currentLocale, *countryKey), country, sizeof(country));

    char* result;
    if (language[0] != '\0' && country[0] != '\0')
    {
        size_t length = strlen(language) + 1 + strlen(country) + 1;
        result = (char*)malloc(length);
        if (result != NULL)
        {
            strcpy(result, language);
            strcat(result, "-");
            strcat(result, country);
        }
    }
    else
    {
        ToUtf8(getCString, getIdentifier(currentLocale), identifier, sizeof(identifier));
        result = CopyString(identifier);
    }
    release(currentLocale);
    return result != NULL ? result : CopyString("");
}

#endif // __APPLE__
