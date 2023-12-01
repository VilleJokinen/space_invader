# GMS Block Store Plugin

GMS Block Store Plugin is Android-specific integration plugin for [Block Store](https://developers.google.com/identity/blockstore/android). Block Store is a feature of GMS (Google Mobile Services) that allows securely storing arbitrary credentials. Block Store allows storing byte-arrays (blocks) both locally on device and backed up to cloud for device-to-device migration. The blocks, or credentials, survive application reinstall.

## Installation into Unity project:

The precompiled library is already included in MetaplaySDK via `MetaplaySDK/Client/Unity/Plugins/Android/metaplayblockstore-release.aar` file

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
cp BuiltLibrary/metaplayblockstore-release.aar ../../Client/Unity/Plugins/Android/metaplayblockstore-release.aar
rm ../../Client/Unity/Plugins/Android/Editor
cp -r BuiltLibrary/Editor ../../Client/Unity/Plugins/Android/
```
