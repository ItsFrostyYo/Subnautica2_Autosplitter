# UE5 Event Reader

This folder is separate from the existing pointer-based inventory, biome,
blueprint, crafting, build, and encyclopedia readers.

## Add events

Open `Subnautica2Ue5EventRegistry.cs` and add definitions inside:

```csharp
#region ADD UE5 EVENTS HERE

Add(
    "MainMenuConstruct",
    "WBP_MainLobbyScreen_C",
    "WBP_MainLobbyScreen_C",
    "Construct");

#endregion ADD UE5 EVENTS HERE
```

No events are configured by default, so the ProcessEvent scanner and hook do
not run until an `Add(...)` call is present.

## Use events

From `Subnautica2Memory.cs` or code with access to the memory instance:

```csharp
if (Ue5EventTriggered("MainMenuConstruct"))
{
    // start, split, reset, or combine with another condition
}

ulong callsThisUpdate = Ue5EventDelta("MainMenuConstruct");
```

The existing pointer readers always update before the optional event registry.
If the event reader fails, only the event registry is disabled.
