#!/bin/bash
set -eEuo pipefail

STATE=$(curl -fsL https://testnet-1.kryolite.io/chainstate)
STATE2=$(curl -fsL http://testnet-2.kryolite.io/chainstate)

HEIGHT=$(echo $STATE | jq ".id")
WEIGHT=$(echo $STATE | jq ".weight" | tr -d '"')
BLOCKS=$(echo $STATE | jq ".blocks")
HEALTH=OK

if [[ $STATE != $STATE2 ]]; then
    HEALTH=DIVERGED
    notify-send "Watchman" "Testnet chains have diverged"
fi

echo "<txt>[Height : "$(($HEIGHT))"] [Weight : "$(($WEIGHT / 1000000))M"] [Blocks : "$(($BLOCKS))"] ["$(echo $HEALTH)"]</txt>"