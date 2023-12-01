#!/bin/bash
set -eo pipefail

# Pre-execute script hook
# \note Commented out due to not being used anywhere -- enable when needed (with necessary tweaks)
# if [[ -z "${PRE_EXECUTE_SCRIPT}" ]]; then
#   echo "No pre-execute script specified"
# else
#   echo "Execute pre-script: ${PRE_EXECUTE_SCRIPT}"
#   bash -c "${PRE_EXECUTE_SCRIPT}"
# fi

# Start the requested application: gameserver or botclient
case $1 in
  "gameserver" )
    echo "STARTING GAME SERVER!"
    cd /gameserver
    exec dotnet Server.dll ${@:2};;
  "botclient" )
    echo "STARTING BOTCLIENT!"
    cd /botclient
    exec dotnet BotClient.dll ${@:2};;
  "dotnet" )
    # Codepath for loadtest chart v0.3.0, can be removed in the future
    echo "STARTING DOTNET APP!"
    # assume working directory has been set from outside
    exec dotnet ${@:2};;
  *)
    echo "Invalid start command '$1' (args ${@:2})"
    exit 1;;
esac
