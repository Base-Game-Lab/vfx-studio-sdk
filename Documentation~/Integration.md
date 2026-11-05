# Adding BaSe Game Lab effects to your game

Each effect arrives as one `.vfxnative` file that already knows your card. This editor-only package turns it into a normal prefab of
Particle Systems, sprites and an Animator on your AllIn1Vfx shader. Nothing of ours ships in your game. There are 3 steps, the first once
only.

## Steps

1. **Install.** Add one line to `Packages/manifest.json`, under `dependencies`:

       "com.basegamelab.vfx": "https://github.com/Base-Game-Lab/vfx-unity.git#v0.1.0",

   The repository is private, so each developer's GitHub account, and any build machine's, needs read access to it. To update,
   change the tag, for example to `#v0.2.0`. A machine without GitHub access can use the tarball we send instead:
   `"com.basegamelab.vfx": "file:com.basegamelab.vfx-0.1.0.tgz"`, with the file in `Packages`.
2. **Drop in the effect.** Put the `.vfxnative` file anywhere under `Assets`. It imports as a prefab with the file's name.
3. **Put it in a slot.** Select the card (the prefab in the Project window, or the card in a scene) and the effect file, then use
   `BaSe VFX > Use as Slot...`, from the `GameObject` menu or the right-click menu of either window. It lists the slots under the card's
   `VFX` node, such as `DefaultOnRevealVFX`, `DefaultDestroy` and `MovedCard_VFX`; press `Use as DefaultOnRevealVFX`. Your code that
   turns that slot on now plays our effect. See "Using a slot" below for what changes.

   Unity's menus are fixed when scripts compile, so the slot names are buttons in that small window rather than menu entries.

## Using a slot

- The slot object stays exactly as it was: its name, its components, and every reference your scripts and prefabs hold to it.
- Its children move, unchanged, under one new inactive child, `_Original`. The effect goes in beside it as an active child, still linked
  to the file, so a new version of the effect still arrives.
- Turning the slot on plays the effect: particles play on awake and its Animator starts its one clip.
- `Edit > Undo` reverses it in one step. Later, `GameObject > BaSe VFX > Restore Original` (or `Restore original` in the window) takes the
  effect out and puts the children back where they were.
- A card prefab, or a slot inside a copy of one, opens in Prefab Mode and the change is made there, so it reaches every card that uses
  the prefab. Prefab Mode's Auto Save saves it, or press Save. A copy's children cannot move, which is Unity's rule for prefabs.
- If the effect moves the card, the window also binds that motion, so activating the slot moves the card with no code. The file holds a
  clip, `<effect> Motion`, for `AdditionalTransform/MainComponents`; the binding adds Unity's own PlayableDirector and needs the Timeline
  package. It binds to the Animator on the card root, even when the slot has an Animator of its own.
- If a slot is moved or scaled, the effect still sits at the card's centre and size, where it was made.
- The slot's own components keep running as before. `DefaultOnRevealVFX` is itself a Particle System, so whatever it drew on its own it
  still draws, beside our effect.
- **A clip that drives the old children by path,** from an Animator or Animancer on the slot or on the card root, for example one that
  turns `DefaultDestroy/Root` on: the path now leads nowhere, because `Root` is under `_Original`. Unity skips a missing path without an
  error, so that clip still plays, the parked children stay off, and the parts of the clip that touch the slot itself, such as turning
  `DefaultDestroy` on, still work and start our effect. The same is true for a script that finds a child by name: it gets nothing. If your
  code turns on a child but never the slot itself, our effect does not start; tell us and we will place it for that child instead.
- Placing the prefab by hand still works: put it under the card's `AdditionalTransform/VFX`, inactive until you want it, and use
  `GameObject > BaSe VFX > Bind Card Motion` if it moves the card.
- An effect you instantiate from code has no binding, so play the motion clip from the card root's Animator, as a state or a Playables
  call.

## How it starts

`SetActive(true)` or `Instantiate`. Particles play on awake and the Animator runs its one 60 fps clip. To replay, turn it off and on.
Like your own effects, nothing turns it off for you.

## First play without a stall

The first time a shader variant draws after an install, the phone compiles it, and that frame is long. Each effect file holds a
**`<effect> warm-up`** sub-asset, a Shader Variant Collection of the variants the effect draws with. Use it one of two ways:

- Add it to **Project Settings > Graphics > Preloaded Shaders**. Unity warms it up when the game starts. No code.
- Or, if your loading step already warms shaders, call `WarmUp()` on it there.

On an iPhone 12 the first play after an install went from a 234 ms frame to 67 ms either way (your own card glow: 150 ms). The warm-up
itself takes about 0.7 s, at start-up or wherever you call it. Later launches were already short (25 to 31 ms), because the phone keeps
its compiled shaders.

## Adjusting an effect

Select the effect file. Its Inspector has four settings under **Adjust**; press Apply and the prefab rebuilds.

- **Speed:** plays the whole effect faster or slower: clips, particle lifetimes, emission, velocities and the card's motion. 2 takes half
  the time.
- **Size:** scales every layer and its distance from the card. Screen-wide dims and the card's own motion stay as they are.
- **Tint:** multiplies every colour. White changes nothing.
- **Intensity:** multiplies the light each layer adds. Dims are left alone. Above 1 only the glowing layers get brighter.

They are stored in the file's `.meta`, so a new version of the effect dropped over the old one under the same name keeps them, and so
does every copy placed in a slot. Make Editable copies the effect as adjusted.

## Changing an effect

- **New version from us:** replace the file, keeping its name. References to the prefab keep working, and so do your adjustments.
- **By hand:** select the file and use `Assets > BaSe VFX > Make Editable`. You get a plain prefab, materials, PNG masks, clips and a
  controller in a new folder. Later imports never touch that copy.
- **Size or sorting:** `Assets > BaSe VFX > Project Settings` makes a settings file that overrides what the effect files carry. Raise
  Anchor Width to make effects larger against the card. Changing it rebuilds every effect.

## What it does not do

- It does not include AllIn1Vfx, so that must be in your project. It adds no runtime code, no components of ours and no build step.
- It has no bloom, distortion or camera shake. The inspector lists anything in a file that did not map, with the reason.
- Layers behind the card stay behind it, so a solid card hides whatever it covers.
- Dims sit inside the card's sorting group, so they darken the board and leave your HUD bright.

## Versions

For Unity 6000.3 with URP. Effects are made for a Linear project: the preview adds light up the way URP does, so the file's colours go
in unconverted. In a Gamma project soft glows come out weaker and dims darker, and the importer's notes say so. The package reads
every `0.x` effect file; a newer file stops with a message asking you to update it.
