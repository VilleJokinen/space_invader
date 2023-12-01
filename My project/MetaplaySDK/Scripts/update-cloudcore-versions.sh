#!/bin/bash

BUILD_NUMBER=$1
COMMIT_ID=$2
FILE_PATH=$3

echo "Updating $FILE_PATH with BuildNumber=\"$BUILD_NUMBER\" and CommitId=\"$COMMIT_ID\""

if [[ $BUILD_NUMBER ]]; then
  sed -i "s/BuildNumber = .*;/BuildNumber = \"$BUILD_NUMBER\";/g" $FILE_PATH
fi

if [[ $COMMIT_ID ]]; then
  sed -i "s/CommitId = .*;/CommitId = \"$COMMIT_ID\";/g" $FILE_PATH
fi
