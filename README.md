# LiveSplit.Subnautica2
This is a Component Livesplit Autosplitter Specific to Subnautica 2,
Based off the Subnautica 1 and Subnautica: Below Zero Autosplitters by Sprinter31.

## Features
- Automatic Survival and Creative Run Starts.
- Automatic Reset when Returning to the Main Menu.
- Intro Lifepod Ascend Cutscene Load Removal using Game Time.
- Customizable and Conditional Auto Splits.
- Specific Set Prefabricated Splits Generalized for Specific Categories.
- Ordered LiveSplit and Auto-Split Modes.

## Version - `1.2.0.2`
- Slight Optimization to Reduce Lag.
- Slight Fix to Build Splits.
- Added Build Processor Prefabricated Split that used Specific Attatched Event that is Consistent.

## Supported Game Versions
- `121347` / 1.1 Hotfix.
- Although its Currently the Only Version Supported, Most Stuff will NOT Break on a Game Update, or just need Slight Tuning, the Game is Still in Early Access so Future Updates might Rework or Add New Things into the Game, which could Break the Autosplitter.

## How to use
1. Open LiveSplit,
2. Right-Click and select `Edit Splits`,
3. Set `Game Name` to `Subnautica 2` and Click Activate.
4. Open the Auto Splitter settings and configure the start, reset, load removal, and split options.

# Settings

## Start / Reset
- `Survival Start` - Starts when the Intro Cinematic Ends, such as when Player Control is Regained or the PDA is Opened.
- `Creative Start` - Starts from the Creative Mode Player-Start Event the Game Uses.
- `Reset` - Resets the Timer when the Main Menu is Loaded.

## Others
- `Warn On Reset If Gold Split` - Asks whether to Save Splits before an Automatic Reset if the Run Contains a Gold Segment Time.
- `Ascend Cutscene-Removal` - Pauses Game Time from the Intro Lifepod Ascend event until the Intro Sequence Ends.
- `Ordered Splits (LiveSplit)` - Only allows the Auto Split Configured for the Current LiveSplit Segment to Trigger.
- `Ordered Splits (Auto-Splits)` - Requires Configured Auto Splits to Trigger in their Listed Order.

## Auto Splits
Available split types include:

- `Prefabricated` - Split on Specific Set Prefabricated Splits Generalized for Specific Categories.
- `Craft` - Split on Crafting something from a Fabricator, Vehicle Fabricator, Modification Station, and Processor Station.
- `Build` - Split on Builder Tool Constructables beinng Completed.
- `Inventory` - Split on Item Pickups, Drops, or Condition Splits to Specify Inventory Count.
- `Blueprint` - Split on Newly Unlocked or Previously Acquired Blueprints/Recipes.
- `Encyclopedia` - Split on Newly Unlocked or Previously Acquired Databank Entries from the Encyclopedia.
- `Story Goal` - Split on Game Specific Story Goal Events that Trigger Throughout Gameplay.
- `Biome` - Split on Biome Transitions or Condition Splits to Specific Biomes

Auto Splits can have Additional Conditions. For Example, a Split can Require the Player to be in a Particular Biome when another Event Occurs. The Gear Button Opens Options such as `Only Split Once` and `Add Condition`.

## Generate Splits
This Button Generates LiveSplit Segments from the Configured Auto Splits. Generating Splits Overwrites the Existing LiveSplit Segments and Times after Confirmation.

# Known Issues
- Build Splits Work but are being Improved for Consistency and can Currently Break by repeated Opening Inventory, Entering Vehicles or Swapping Menus before Completing the Build, Direct Builds after Placement should Consistently Split.
- Game Updates May Break Memory Offsets, Unreal Engine Field Layouts, or Registered Events, Version `121347` will Consistently Work.
- Restarting the Game May Rarely Break the Autosplitter. If this Happens, Restart LiveSplit or Reload the Autosplitter.

# Contributing
Bug Reports are Highly Encouraged and Improvements are Welcome.
