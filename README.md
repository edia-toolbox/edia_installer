# EDIA Installer 

A one-script repo.  

Single Editor-script which allows handling the installation of the various EDIA modules and their core XR dependecies (including loading relevant samples).  

Covers every EDIA module currently shipped from [edia-toolbox](https://github.com/edia-toolbox): **EDIA UXF**, **EDIA Core**, **EDIA LSL**, **EDIA Rcas**, **EDIA Survey**, **EDIA Eye** and its headset-specific sub-modules (PICO, Quest, Varjo, Vive). Selecting a headset module automatically also selects EDIA Eye and EDIA Core, since those are required; EDIA Core in turn pulls in EDIA UXF.

<img width="511" alt="EDIA Installer window" src="./docs/installer-window.png" />

## Installation

**Recommended:** download the [unitypackage](./Packages/EdiaInstaller.unitypackage) and import it into your Unity project (menu: `Assets > Import Package > Custom Package`). The installer window opens by itself once Unity has compiled the imported script. It is also always available from the menu bar:

<img width="120" alt="EDIA > Installer menu entry" src="./docs/installer-menu.png" />

Alternatively, clone this repo -> open **"EDIA > Installer"** from the main menu, and take it from there to build a project from scratch.

> [!IMPORTANT]  
> You need _not_ install any of EDIA's dependencies manually — the installer takes care of them. Do make sure you do **not** have the upstream UXF installed in the project: it conflicts with EDIA's own UXF fork, which the installer brings in.

## How it works

The window walks through five steps. The first three install what is missing and are each gated on the previous one; the last two are settings you apply yourself, because they are project-specific and overwriting them blindly would be worse than asking.

1. **XR Dependencies** — installs Unity's XR Interaction Toolkit and XR Hands, plus XR Plugin Management and the OpenXR provider. The toolkits only provide the interaction layer; without a provider Unity does not consider the project XR-ready and no headset is picked up.
2. **Required Samples** — imports the samples the EDIA rig reuses: XRI *Starter Assets*, *Hands Interaction Demo* and *XR Device Simulator*, plus XR Hands' *Hand Visualizer*. Without these the rig has broken references. Also imports TextMeshPro's essential resources, which EDIA's UI needs and Unity ships as a separate one-time import.
3. **EDIA Packages** — tick the modules you want. Dependencies are selected automatically and shown locked.
4. **Project Validation** — opens Unity's own Project Validation page, where the remaining project settings (target platform, rendering, the interaction profiles for your headset) are listed with a Fix button each. Which ones you need depends on your hardware, so the installer does not write them for you.
5. **EDIA Configurator** — opens the Configurator so you can press *Setup layers*. EDIA's rig and UI depend on their own layers; creating them is not automated because that could overwrite layers your project already uses.

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
- [x] cover the XR provider, TextMeshPro essentials and the manual project settings
- [ ] drop the forced `EDIA UXF` dependency once a Core release ships with UXF absorbed
