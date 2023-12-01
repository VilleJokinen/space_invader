// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#import <Foundation/Foundation.h>
#import <GameKit/GameKit.h>

typedef void (*GetLegacySocialReturnCB)(
    const char* legacyPlayerId,
    const char* legacyPublicKeyUrl,
    const char* legacySaltBase64,
    const char* legacySignatureBase64,
    uint64_t legacyTimestamp,
    const char* bundleId,
    const char* errorString
    );

extern void MetaplayGameCenter_GetSocialClaimLegacy(GetLegacySocialReturnCB cb)
{
    GKLocalPlayer* localPlayer = [GKLocalPlayer localPlayer];
    NSString* bundleIdUnscoped = [[NSBundle mainBundle] bundleIdentifier];

    if (!localPlayer.authenticated)
    {
        cb(NULL, NULL, NULL, NULL, 0, NULL, "not logged in into GameCenter");
        return;
    }

    // \note: Check if the symbol is available before using it
    if (@available(iOS 14, *))
    {
        if (&GKPlayerIDNoLongerAvailable != NULL)
        {
            NSString* legacyPlayerId = localPlayer.playerID;
            if ([legacyPlayerId isEqualToString:GKPlayerIDNoLongerAvailable])
            {
                cb(NULL, NULL, NULL, NULL, 0, NULL, "player id is no longer available");
                return;
            }
        }
    }

    [localPlayer generateIdentityVerificationSignatureWithCompletionHandler:^(NSURL* publicKeyURL, NSData* signature, NSData* salt, uint64_t legacyTimestamp, NSError* error)
    {
        // \note: Autorelease pool is required to prevent ARC releasing String containers before the underlying UTF8 string has been delivered to Callback
        @autoreleasepool
        {
            // \note: Nil objects return nil results.
            NSString* bundleId = [NSString stringWithString:bundleIdUnscoped];
            NSString* legacyPublicKeyURLString = [publicKeyURL absoluteString];
            NSString* legacySignatureString  = [signature base64EncodedStringWithOptions:0];
            NSString* legacySaltString = [salt base64EncodedStringWithOptions:0];
            NSString* errorDescription = nil;

            if (error != nil)
                errorDescription = [NSString stringWithString: [error description]];

            cb(
                [localPlayer.playerID UTF8String],
                [legacyPublicKeyURLString UTF8String],
                [legacySaltString UTF8String],
                [legacySignatureString UTF8String],
                legacyTimestamp,
                [bundleId UTF8String],
                [errorDescription UTF8String]);
        }
    }];
}

typedef void (*GetWWDC2020SocialReturnCB)(
    const char* wwdc2020TeamPlayerId,
    const char* wwdc2020GamePlayerId,
    const char* wwdc2020PublicKeyUrl,
    const char* wwdc2020SaltBase64,
    const char* wwdc2020SignatureBase64,
    uint64_t wwdc2020Timestamp,
    const char* bundleId,
    const char* errorString
    );

extern void MetaplayGameCenter_GetSocialClaimWWDC2020(GetWWDC2020SocialReturnCB cb)
{
    GKLocalPlayer* localPlayer = [GKLocalPlayer localPlayer];
    NSString* bundleIdUnscoped = [[NSBundle mainBundle] bundleIdentifier];

    if (!localPlayer.authenticated)
    {
        cb(NULL, NULL, NULL, NULL, NULL, 0, NULL, "not logged in into GameCenter");
        return;
    }

    if (![localPlayer respondsToSelector:@selector(fetchItemsForIdentityVerificationSignature:)])
    {
        cb(NULL, NULL, NULL, NULL, NULL, 0, NULL, "no platform available");
        return;
    }

    if (@available(iOS 13, *))
    {
        if (![localPlayer scopedIDsArePersistent])
        {
            cb(NULL, NULL, NULL, NULL, NULL, 0, NULL, "scoped ids are not persistent");
            return;
        }
    }

    if (@available(iOS 13.5, *))
    {
        [localPlayer fetchItemsForIdentityVerificationSignature:^(NSURL* publicKeyURL, NSData* signature, NSData* salt, uint64_t timestamp, NSError* error)
        {
            // \note: Autorelease pool is required to prevent ARC releasing String containers before the underlying UTF8 string has been delivered to Callback
            @autoreleasepool
            {
                // \note: Nil objects return nil results.
                NSString* bundleId = [NSString stringWithString:bundleIdUnscoped];
                NSString* publicKeyURLString = [publicKeyURL absoluteString];
                NSString* signatureString  = [signature base64EncodedStringWithOptions:0];
                NSString* saltString  = [salt base64EncodedStringWithOptions:0];

                NSString* errorDescription = nil;
                if (error != nil)
                    errorDescription = [NSString stringWithString: [error description]];

                cb(
                    [localPlayer.teamPlayerID UTF8String],
                    [localPlayer.gamePlayerID UTF8String],
                    [publicKeyURLString UTF8String],
                    [saltString UTF8String],
                    [signatureString UTF8String],
                    timestamp,
                    [bundleId UTF8String],
                    [errorDescription UTF8String]);
            }
        }];
    }
    else
    {
        cb(NULL, NULL, NULL, NULL, NULL, 0, NULL, "not supported");
        return;
    }
}
