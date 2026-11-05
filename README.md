# BaSe Game Lab VFX

Effects from VFX Studio, as normal Unity prefabs. This package is editor only: nothing of ours ships in your game.

## Install

In Unity, open `Window > Package Manager`, press `+`, choose `Install package from git URL...` and paste:

    https://github.com/Base-Game-Lab/vfx-studio-sdk.git#v0.3.8

Or add one line to `Packages/manifest.json`, under `dependencies`:

    "com.basegamelab.vfx": "https://github.com/Base-Game-Lab/vfx-studio-sdk.git#v0.3.8",

The repository is public, so no GitHub account or sign-in is needed; Unity only needs `git` installed. The same goes for any build
machine. Being public grants no rights to use it: see License below.

Check it worked: the Package Manager lists `BaSe Game Lab VFX` with the version you asked for.

No git on a machine: we send `com.basegamelab.vfx-0.3.8.tgz`; copy it to `Packages` and use
`"com.basegamelab.vfx": "file:com.basegamelab.vfx-0.3.8.tgz"` instead.

AllIn1Vfx Toolkit must be in the project. Materials reference its shader; the package does not bundle it.

## Update

Change the tag at the end of the line, for example `#v0.3.2` to `#v0.3.8`, and return to Unity (or press `Update` on the package in
the Package Manager). Each version is a tag on this
repository; the tag never moves, so every machine on the same line gets the same package.

## Make an effect for one of your cards

1. In the **Hierarchy** (not the Project window: its `BaSe VFX` menu only has Put in a Slot), right-click the slot the effect goes in, under the card's `VFX` node (for example `DefaultOnRevealVFX`), and choose
   `BaSe VFX > Send to VFX Studio...`. It plays the card in the editor (a progress bar counts the frames) and saves one `.vfxcapture`
   file. Your scene is not changed.
2. A `Capture ready` window shows the file and what the studio will note about it. It stays open: use `Open VFX Studio` (or go to
   https://portal.basegamelab.com/tools/vfx-studio/; we give you a login) and `Show in Explorer`, then drag the file from the folder onto
   the page. Your card opens on its own board with our effects already playing on it. Pick one, tune it, and use
   the Unity export (`Export > Unity for ...`).
3. Drag the `.vfxnative` file into the **Project window**, into any folder under `Assets` (not onto the Scene view, which only takes
   files Unity has already imported). It goes into the slot it was captured from by itself. `Edit > Undo` or
   `GameObject > BaSe VFX > Put Back Original` takes it out. If that slot is gone, the Console says why, and you place it as below.

A slot's right-click `BaSe VFX` menu has the round trip: `Send to VFX Studio...`, `Import from Studio...` and `Put Back Original`.

## Use

1. In the Hierarchy, right-click the slot under the card's `VFX` node, such as `DefaultOnRevealVFX`, and choose
   `BaSe VFX > Import from Studio...`. A small window for that slot lists the effects already in the project, newest first, with the ones
   captured from this slot in bold; press `Use` on one. Or press `From file...` and pick a `.vfxnative` (it starts in Downloads): it is
   copied into `Assets/BaSe VFX`, imported, and put in this slot only, even when it was captured from another one.
2. Starting from the effect file instead: right-click it in the Project window, `BaSe VFX > Put in a Slot...`, set the card, and press
   `Use as <slot>`.
3. Turn the slot on, as your code already does. Particles and sprites play by themselves, with no script from us. A one-shot effect
   ends invisible: anything the effect still shows at its last frame (a reveal's dimmed screen, say) fades out over 0.3s after it, so
   the game looks as it did before.

Placing an effect keeps the slot object, its name, its components and every reference to it. It parks the slot's children under one inactive
child, `_Original`, and nests the effect beside it, still linked to the file. `Edit > Undo` reverses it in one step;
`GameObject > BaSe VFX > Put Back Original` puts the children back later. A clip or script that reaches the old children by path, such as
`DefaultDestroy/Root`, finds nothing now and Unity skips it. A card prefab opens in Prefab Mode and the change is made there. An effect
that moves the card is bound to the card's Animator on the way in (needs Timeline); its Timeline is a second file, so the effect
moves into a folder named after it with the Timeline beside it, keeping its GUID and every reference. The details are in `Documentation~/Integration.md`.

You can still place the prefab by hand: put it where your effects live, for example under a card's `VFX` node, inactive.

To play again, deactivate and activate it. The prefab is built the usual way: Particle Systems, sprites with AllIn1Vfx
materials, grey masks, and an Animator with one 60 fps clip.

To avoid a long frame the first time an effect plays after an install, add the effect file's `<effect> warm-up` sub-asset to Project
Settings > Graphics > Preloaded Shaders (or call `WarmUp()` on it in your loading step). See `Documentation~/Integration.md`.

## Adjust

Each effect file's Inspector has **Speed**, **Size**, **Tint** and **Intensity**; press Apply and the prefab rebuilds. Speed scales every
time (clips, lifetimes, emission, velocities, card motion), Size every layer's size and distance from the card (not screen-wide dims or the
card's motion), Tint multiplies every colour (white is none), Intensity the light each layer adds (dims are left alone). They are kept in the
file's `.meta`, so they survive a new version of the file dropped in under the same name.

## Settings

None needed: each effect file carries the card's width, sorting, render queues and paths for your game. To override them,
`Assets > BaSe VFX > Project Settings` opens `Assets/VfxNativeSettings.asset`, creating it if needed from the values of the newest effect
in the project (or, from an effect's Inspector, that effect), so creating it changes nothing. It must stay at that path, and when it
exists it replaces every value in the files. Where it differs from what an effect carries, that effect's import says so in the Console
and its Inspector. To go back to the effects' own values, delete the file.

- `Anchor Width`: width of the effect's anchor (the card) in local units. Effects scale from this.
- `Screen Size`: the visible screen in those same units, so flat dims and flashes cover it. Zero uses the effect's own frame.
- `Sorting Layer`, `Order Behind`, `Order Front`: where the effect sorts against the card.
- `Queue Behind`, `Queue Front`: render queues of layers behind and in front of the card, for a game that sorts card parts by queue.
- `Backdrop`: only for effects exported before format 0.3, the colour they usually land on, as shown on screen. Newer files need nothing.
- `Targets`: the path of each object an effect moves, from the Animator above it, for example `card` to the node holding the card's
  art. Pick a node that does not contain the effect, or the effect moves and scales with it.

Changing the settings rebuilds every effect.

## Effects that move a card

An Animator only reaches objects below it, and the prefab sits below the card. So card motion comes as a second clip,
`<effect> Motion`, inside the file, bound to your `Targets` path. Two ways to play it:

1. No code, for an effect placed in your card prefab: select the effect under the card and use `GameObject > BaSe VFX > Bind
   Card Motion`. It adds Unity's own PlayableDirector, so activating the effect also moves the card. Needs Timeline.
2. Your own way: the clip is a normal AnimationClip, relative to the Animator on your card root, so a state or a Playables call
   of yours plays it. Use this for an effect you instantiate from code, because a PlayableDirector's binding is a reference
   stored in the card prefab.

Everything else in the prefab plays on activation with no wiring.

If a bound effect is turned off before it ends, the card stays where the clip left it, because nothing of ours runs to put it
back. Let it finish, or reset the moved node yourself when you cut it.

## What did not map

The inspector lists anything the exporter could not turn into Unity parts, per effect, with the reason. The same text is
inside the file as `<effect> notes`.

## Changing an effect

The imported prefab is read-only and keeps its IDs across re-imports, so references never break. To change it by hand,
select the file and use `Assets > BaSe VFX > Make Editable` (or the inspector button). That writes plain assets into a new
folder beside it: prefab, materials, PNG masks, clips, controller. A later re-import never touches that copy.

## Versions

The package reads every `0.x` file it knows, forever. A newer file stops with a plain message telling you to update the
package.

## See your slots play

`Window > BaSe VFX > Slot Player` lists the slots of every card on the board (the one you have selected first; `Show inactive` adds the rest) with a `Fire` button each. In Play mode, Fire turns
the slot off and on and plays its particle systems, as your code would, so you can watch an effect without playing to that moment
in the game. A slot that your code also recolours or fills in at runtime can still show nothing here.

## License

This package is proprietary and licensed only to organisations with a current VFX Studio agreement with BaSe Game Lab. See
[LICENSE.md](LICENSE.md) for the full terms; the repository being public grants no rights to use it.
