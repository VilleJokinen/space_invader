// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#ifdef IS_WEBAUTH_ENABLED

#import <Foundation/Foundation.h>
#import <AuthenticationServices/AuthenticationServices.h>

extern UIViewController* UnityGetGLViewController(void);

@interface UnityWebAuthSessionContextProvider : NSObject <ASWebAuthenticationPresentationContextProviding>
+ (UnityWebAuthSessionContextProvider*)sharedInstance;
@property (strong) ASWebAuthenticationSession* ongoingSession API_AVAILABLE(ios(12.0));
@end
@implementation UnityWebAuthSessionContextProvider

static UnityWebAuthSessionContextProvider* s_sharedInstance;

@synthesize ongoingSession;

- (ASPresentationAnchor)presentationAnchorForWebAuthenticationSession:(ASWebAuthenticationSession *)session NS_AVAILABLE_IOS(13_0)
{
    return UnityGetGLViewController().view.window;
}
+ (void)initialize
{
    s_sharedInstance = [[UnityWebAuthSessionContextProvider alloc] init];
}
+ (UnityWebAuthSessionContextProvider*)sharedInstance
{
    return s_sharedInstance;
}
@end

#endif

typedef void (*AuthenticateCB)(
    const char* callbackUrl,
    const char* errorString
    );

extern void MetaplayIosWebAuthenticationSession_Authenticate(const char* url, const char* cbScheme, AuthenticateCB cb)
{
#ifdef IS_WEBAUTH_ENABLED
    NSURL* urlUrl = [NSURL URLWithString:[NSString stringWithUTF8String:url]];
    NSString* cbSchemeString = [NSString stringWithUTF8String:cbScheme];

    if (@available(iOS 12, *))
    {
        ASWebAuthenticationSession* session = [[ASWebAuthenticationSession alloc] initWithURL:urlUrl callbackURLScheme:cbSchemeString completionHandler:^(NSURL *callbackURL, NSError *error)
        {
            // Session is no longer needed, clear the global reference
            [[UnityWebAuthSessionContextProvider sharedInstance] setOngoingSession:nil];

            // \note: Autorelease pool is required to prevent ARC releasing String containers before the underlying UTF8 string has been delivered to Callback
            @autoreleasepool
            {
                // \note: Nil objects return nil results.
                NSString* callbackURLString = [callbackURL absoluteString];
                NSString* errorDescription = nil;

                if (error != nil)
                    errorDescription = [NSString stringWithString: [error description]];

                cb(
                    [callbackURLString UTF8String],
                    [errorDescription UTF8String]);
            }
        }];

        if (@available(iOS 13, *))
        {
            session.presentationContextProvider = [UnityWebAuthSessionContextProvider sharedInstance];
            session.prefersEphemeralWebBrowserSession = false;
        }

        if ([session start])
        {
            // Take strong reference into the session
            [[UnityWebAuthSessionContextProvider sharedInstance] setOngoingSession:session];
        }
        else
        {
            cb(NULL, "failed to start WebAuthenticationSession");
        }
    }
    else
    {
        cb(NULL, "not supported");
    }
#else
    (void)url;
    (void)cbScheme;
    cb(NULL, "not enabled");
#endif
}
