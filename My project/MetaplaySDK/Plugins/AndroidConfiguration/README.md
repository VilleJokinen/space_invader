# Android Configuration Plugin

Android Configuration Plugin is Android-specific integration plugin that allows MetaplaySDK to access various
configuration and platform values, such as SSAID.

## Installation into Unity project:

The precompiled library is already included in MetaplaySDK via `MetaplaySDK/Client/Unity/Plugins/Android/metaplayandroidconfiguration-release.aar` file

----------------------------------

## Build instructions for Library.

MetaplaySDK ships with prebuilt library and manually building the library should not be required.
However, if library needs to be customized or targeted to a certain Editor version, it can rebuilt as follows:

1. Install OpenJDK.
    * Minimum supported JDK version is 12
    * Latest supported JDK version is 19.
    * Unity's Builtin JDK is not in this version range and is not supported.
2. Set JAVA_HOME environment variable.
3. Install Android SDK
4. Set ANDROID_HOME environment variable
5. Install Unity Editor with Android build support
6. Set UNITY_EDITOR_HOME environment variable to point to the Editor's installation folder, for example `.../Editor/2021.3.19f1`
7. Run `.\gradlew buildLib` in Lib folder
    * Gradle may automatically download missing Android SDK components. To use these components, you may need to accept license agreements.
8. Copy compiled library from `BuiltLibrary` folder to `MetaplaySDK/Client/Unity/Plugins/Android/`.


Powershell Example:
```
$env:JAVA_HOME="C:\Program Files\Microsoft\jdk-17.0.6.10-hotspot\"
$env:ANDROID_HOME="C:\Users\Jarkko\Documents\androidsdk"
$env:UNITY_EDITOR_HOME="C:\\Program Files\\Unity\\Hub\\Editor\\2021.3.19f1"
cd Lib
.\gradlew buildLib
cd ..
cp BuiltLibrary/metaplayandroidconfiguration-release.aar ../../Client/Unity/Plugins/Android/metaplayandroidconfiguration-release.aar
```
