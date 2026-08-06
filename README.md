# EDIA Installer 

A one-script repo.  

Single Editor-script which allows handling the installation of the various EDIA modules and their core XR dependecies (including loading relevant samples).  

Covers every EDIA module currently shipped from [edia-toolbox](https://github.com/edia-toolbox): **EDIA UXF**, **EDIA Core**, **EDIA LSL**, **EDIA Rcas**, **EDIA Survey**, **EDIA Eye** and its headset-specific sub-modules (PICO, Quest, Varjo, Vive). Selecting a headset module automatically also selects EDIA Eye and EDIA Core, since those are required; EDIA Core in turn pulls in EDIA UXF.

<img width="511" alt="EDIA Installer window" src="./docs/installer-window.png" />

## Installation

**Recommended:** download the [unitypackage](./Packages/EdiaInstaller.unitypackage) and import it into your Unity project (menu: `Assets > Import Package > Custom Package`). This gives you the following entry in your menu bar:

<img width="120" alt="EDIA > Installer menu entry" src="./docs/installer-menu.png" />

Alternatively, clone this repo -> open **"EDIA > Installer"** from the main menu, and take it from there to build a project from scratch.

> [!IMPORTANT]  
> You need _not_ install any of EDIA's dependencies manually — the installer takes care of them. Do make sure you do **not** have the upstream UXF installed in the project: it conflicts with EDIA's own UXF fork, which the installer brings in.

## How it works

The window walks through three steps, each gated on the previous one:

1. **XR Dependencies** — installs Unity's XR Interaction Toolkit and XR Hands.
2. **Required Samples** — imports the samples the EDIA rig reuses: XRI *Starter Assets*, *Hands Interaction Demo* and *XR Device Simulator*, plus XR Hands' *Hand Visualizer*. Without these the rig has broken references.
3. **EDIA Packages** — tick the modules you want. Dependencies are selected automatically and shown locked.

Per module you can specify any git ref in the **branch** field: a branch name (`main`, `dev`) or a release tag (`v0.6.0`). It is passed to the Package Manager as-is.

Bring some patience after clicking `Install`. Unity takes a while (for anything), and installing packages triggers domain reloads — the installer resumes its queue automatically after each one. Wait for the final domain refresh before judging any remaining console errors.

## Releasing

`Packages/EdiaInstaller.unitypackage` is re-exported automatically whenever `Assets/Editor/EdiaInstaller.cs` changes, so the committed package stays in sync with the script. Pushing a tag matching `release-*` publishes a GitHub Release with that package attached (see `.github/workflows/make_package_release.yml`).

## Roadmap
- [x] add `EDIA Eye` submodules
  - [x] Quest
  - [x] PICO
  - [x] Vive
  - [x] Varjo
- [x] add `EDIA RCAS`
- [x] add `EDIA Survey`
- [x] allow to install releases (not only branches) 
- [ ] drop the forced `EDIA UXF` dependency once a Core release ships with UXF absorbed
