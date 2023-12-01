# Android Custom Tabs Plugin

Android Custom Tabs Plugin is an Android-specific integration plugin for [Custom Tabs](https://developer.android.com/reference/androidx/browser/customtabs/package-summary). Custom Tabs is a protocol that allows displaying the default device browser within the app with app-specific theming. Unlike WebViews, the browser is running in an isolated process and may use the device default browsers cookie jar. Sharing the cookie jar allows Custom Tab to be automatically logged in into web services if the device default browser was already logged in into those services.

## Installation into Unity project:

**1. Ensure *External Dependency Manager* is installed.**

*External Dependency Manager for Unity* (EDM4U) is a tool that manages Android application dependencies. It automatically downloads necessary packages and resolves any version conflicts.

To install EDM4U as a Unity Package Manager Package:
 * Download tgz from https://developers.google.com/unity/archive#external_dependency_manager_for_unity
 * Follow instructions in https://developers.google.com/unity/instructions#open-close-manifest

Note that if EDM4U is installed via .unitypackage (and not Unity Package Manager Package), `METAPLAY_HAS_GOOGLE_EDM4U` symbol must be defined:
 * In Unity Editor -> Menu -> Project Settings -> Player -> Android -> Other Settings -> Script Define Symbols
    * Add METAPLAY_HAS_GOOGLE_EDM4U
    * Remember to press Apply

**2. Install Package/package.json via Unity Package Manager**

Custom Tabs Plugin is packaged as a Unity Package Manager package to allow adding and removing it on demand.

To add it:
 * Choose Menu -> Window -> Package Manager
 * Select "Add Package From Disk"
 * Navigate to `MetaplaySDK/Plugins/AndroidCustomTabs/Package/package.json`.

Note that Unity may automatically use Absolute paths in `Packages/manifest.json`. To avoid issues with version control, convert the path to relative form, such as `file://MetaplaySDK/Plugins/AndroidCustomTabs/Package`.

**3. Let External Dependency Manager resolve new dependencies**

Finally, we update the dependencies using EDM4U and update the dependencies:
  * Select Menu -> Assets -> External Dependency Manager -> Android Resolver -> Resolve.
    * If EDM4U resolution fails with JAVA_HOME missing or with `Could not initialize class org.codehaus.groovy.vmplugin.v7.Java7` error:
        * Install Unity JDK package in Unity Hub
        * Edit -> Preferences -> External Tools -> JDK path
            * Uncheck "JDK installed with Unity (recommended)"
            * Recheck "JDK installed with Unity (recommended)"
            * Try again
* EDM4U updates dependencies in Plugins/Android. If there are any changes, commit these into version control.

Installation is now complete.

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
8. Commit changes in CompiledArchive folder to version control.


Powershell Example:
```
$env:JAVA_HOME="C:\Program Files\Microsoft\jdk-17.0.6.10-hotspot\"
$env:ANDROID_HOME="C:\Users\Jarkko\Documents\androidsdk"
$env:UNITY_EDITOR_HOME="C:\\Program Files\\Unity\\Hub\\Editor\\2021.3.19f1"
cd Lib
.\gradlew buildLib
cd ..
cd Package
git add CompiledArchive

# Let External Dependency Manager resolve dependencies and update them too.
cd <project root>
git add Assets/Plugins/...
```
