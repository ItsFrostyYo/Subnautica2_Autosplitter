# LiveSplit.Subnautica
This LiveSplit Auto Splitter provides automatic start, split, reset, and load time removal for Subnautica by tracking in-game memory values.

## Features
- Automatic run start for any game mode
- Customizable Auto Splits
- Conditional Auto Splits
- Automatic reset on returning to the main menu
- Portal load time removal (used in creative runs)
- Aurora explosion time component

## Supported Game Versions
- September 2018
- March 2023
- August 2025
- October 2025

Other game versions may work as well or partially

## How to use
1. Open LiveSplit
2. Right-click → Edit Splits
3. Set *Game Name* to Subnautica
4. Activate the Auto Splitter
5. Open Settings and configure split options

If you are playing a Creative category, you can use portal load time removal.
To enable it, right-click LiveSplit, go to Compare Against, and select Game Time.

# Settings

## Start / Reset
- Start after intro – Starts the timer when the Lifepod radio gets damaged
- Creative Start – Starts the timer when you move horizontally, jump, open your PDA or interact with the Fabricator
- Reset – Resets the timer when the main menu is shown

## Others
- Warn On Reset if Gold – Shows a warning before automatically resetting when better times
- SRC Loadtimes – When using in-game time this setting will add time to the actual load times to match the load times estimated by the moderators on speedrun.com (may be inaccurate)
- Ordered Splits (LiveSplit) – Auto Splits will get assigned to the LiveSplit splits and will only auto split when the corresponding LiveSplit split is active
- Ordered Splits (Auto-Splits) – Auto Splits will trigger one after another in predefined order

## Auto Splits
Auto splits can have additional conditions, such as being in a specific biome while crafting a specific item
Some auto splits are further configurable, such as Inventory auto splits

## Generate Splits
This button generates LiveSplit splits based on the Auto Splits you have configured

## Explosion Time
Adds a LiveSplit text component to your layout which displays the time it takes for the Aurora to explode (used in glitchless runs)

# Known Issues
- Game updates may temporarily break memory signatures
- Restarting the game may rarely break the Auto Splitter
If this happens, restart LiveSplit or reload the Auto Splitter

# Contributing
Bug reports and code improvements are welcome
