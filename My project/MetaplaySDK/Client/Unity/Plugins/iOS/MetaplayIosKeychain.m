// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#import <Foundation/Foundation.h>
#import <Security/Security.h>

extern int MetaplayIosKeychain_SetGenericPassword(int storage, const char* service, const char* account, const unsigned char* bytes, int numBytes)
{
    NSString* accessible = (storage == 1) ? ((__bridge NSString*)kSecAttrAccessibleAfterFirstUnlock) : ((__bridge NSString*)kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly);
    NSNumber* synchronizable = (storage == 1) ? [NSNumber numberWithBool:YES] : [NSNumber numberWithBool:NO];
    NSData* data = [NSData dataWithBytes:bytes length:numBytes];

    NSMutableDictionary* query = [[NSMutableDictionary alloc] init];
    [query setObject:(__bridge id)kSecClassGenericPassword forKey:(__bridge id)kSecClass];
    [query setObject:[NSString stringWithUTF8String: service] forKey:(__bridge id)kSecAttrService];
    [query setObject:[NSString stringWithUTF8String: account] forKey:(__bridge id)kSecAttrAccount];
    [query setObject:accessible forKey:(__bridge id)kSecAttrAccessible];
    [query setObject:synchronizable forKey:(__bridge id)kSecAttrSynchronizable];
    [query setObject:data forKey:(__bridge id)kSecValueData];

    if (@available(iOS 13, *))
    {
        // Recommended for MacOS compat
        [query setObject:[NSNumber numberWithBool:YES] forKey:(__bridge id)kSecUseDataProtectionKeychain];
    }

    OSStatus result = SecItemAdd((__bridge CFDictionaryRef)query, nil);
    if (result == errSecDuplicateItem)
    {
        [query removeObjectForKey:(__bridge id)kSecValueData];

        NSMutableDictionary* attrsToUpdate = [[NSMutableDictionary alloc] init];
        [attrsToUpdate setObject:data forKey:(__bridge id)kSecValueData];

        result = SecItemUpdate((__bridge CFDictionaryRef)query, (__bridge CFDictionaryRef)attrsToUpdate);
    }

    return result;
}

extern int MetaplayIosKeychain_GetGenericPassword(int storage, const char* service, const char* account, unsigned char** outBytes, int* outNumBytes)
{
    NSNumber* synchronizable = (storage == 1) ? [NSNumber numberWithBool:YES] : [NSNumber numberWithBool:NO];

    NSMutableDictionary* query = [[NSMutableDictionary alloc] init];
    [query setObject:(__bridge id)kSecClassGenericPassword forKey:(__bridge id)kSecClass];
    [query setObject:[NSString stringWithUTF8String: service] forKey:(__bridge id)kSecAttrService];
    [query setObject:[NSString stringWithUTF8String: account] forKey:(__bridge id)kSecAttrAccount];
    [query setObject:synchronizable forKey:(__bridge id)kSecAttrSynchronizable];
    [query setObject:(__bridge id)kSecUseAuthenticationUISkip forKey:(__bridge id)kSecUseAuthenticationUI];
    [query setObject:(__bridge id)kSecMatchLimitOne forKey:(__bridge id)kSecMatchLimit];
    [query setObject:[NSNumber numberWithBool:YES] forKey:(__bridge id)kSecReturnData];

    CFDataRef cfdata = nil;
    OSStatus result = SecItemCopyMatching((__bridge CFDictionaryRef)query, (CFTypeRef*)&cfdata);

    /* Not found is special case. Return success. */
    if (result == errSecItemNotFound)
    {
        *outBytes = NULL;
        *outNumBytes = 0;
        return 0;
    }

    if (result != 0)
    {
        *outBytes = NULL;
        *outNumBytes = 0;
        return result;
    }

    int numBytes = (int)CFDataGetLength(cfdata); /* Note: Length won't be over 2GB. Can't overflow. */
    unsigned char* returnBuffer = malloc(numBytes);
    memcpy(returnBuffer, CFDataGetBytePtr(cfdata),  numBytes);
    CFRelease(cfdata);

    *outBytes = returnBuffer;
    *outNumBytes = numBytes;

    return 0;
}

extern void MetaplayIosKeychain_GetGenericPassword_ReleaseBuf(unsigned char* buffer)
{
    free(buffer);
}

extern int MetaplayIosKeychain_ClearGenericPassword(int storage, const char* service, const char* account)
{
    NSNumber* synchronizable = (storage == 1) ? [NSNumber numberWithBool:YES] : [NSNumber numberWithBool:NO];

    NSMutableDictionary* query = [[NSMutableDictionary alloc] init];
    [query setObject:(__bridge id)kSecClassGenericPassword forKey:(__bridge id)kSecClass];
    [query setObject:[NSString stringWithUTF8String: service] forKey:(__bridge id)kSecAttrService];
    [query setObject:[NSString stringWithUTF8String: account] forKey:(__bridge id)kSecAttrAccount];
    [query setObject:synchronizable forKey:(__bridge id)kSecAttrSynchronizable];

    OSStatus result = SecItemDelete((__bridge CFDictionaryRef)query);

    return result;
}
