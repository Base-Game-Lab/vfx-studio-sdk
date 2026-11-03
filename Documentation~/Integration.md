# Adding BaSe Game Lab effects to your game

Each effect arrives as one `.vfxnative` file that already knows your card. This editor-only package turns it into a normal prefab of
Particle Systems, sprites and an Animator on your AllIn1Vfx shader. Nothing of ours ships in your game. There are 3 steps, the first once
only, and a fourth for an effect that moves the card.

## Steps

1. **Install.** Add one line to `Packages/manifest.json`, under `dependencies`:

       "com.basegamelab.vfx": "https://github.com/Base-Game-Lab/vfx-unity.git#v0.1.0",

   The repository is private, so each developer's GitHub account, and any build machine's, needs read access to it. To update,
   change the tag, for example to `#v0.2.0`. A machine without GitHub access can use the tarball we send instead:
   `"com.basegamelab.vfx": "file:com.basegamelab.vfx-0.1.0.tgz"`, with the file in `Packages`.
2. **Drop in the effect.** Put the `.vfxnative` file anywhere under `Assets`. It imports as a prefab with the file's name.
3. **Place it.** Put the prefab under the card's `AdditionalTransform/VFX`, next to `DefaultOnRevealVFX`, inactive until you want it.
4. **If the effect moves the card.** The file also holds a clip, `<effect> Motion`, that moves `AdditionalTransform/MainComponents` and
   leaves the effect still. Without this step the effect plays and the card stays put.
   - Effect placed in the card prefab: select it and use `GameObject > BaSe VFX > Bind Card Motion`. No code. It adds Unity's own
     PlayableDirector and needs the Timeline package.
   - Effect instantiated from code: play the clip from the card root's Animator, as a state or a Playables call.

## How it starts

`SetActive(true)` or `Instantiate`. Particles play on awake and the Animator runs its one 60 fps clip. To replay, turn it off and on.
Like your own effects, nothing turns it off for you.

## Changing an effect

- **New version from us:** replace the file, keeping its name. References to the prefab keep working.
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
