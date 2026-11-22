# Mandelbrot.Visualizer

An interactive viewer for the Mandelbrot set and its relatives. Every pixel asks the same question: if you follow the rule `z = z^n + c` over and over, do the numbers stay finite or run off to infinity?

`n` can be any real number, not only 2. The panel edits the power, the iteration limit, the coloring, and whether you are looking at a Mandelbrot-style map or a Julia set. The picture can be drawn on the CPU or with a GPU shader.

## Capabilities

- Switch between five fractal families (`Multibrot`, `Burning Ship`, `Tricorn`, `Celtic`, `Buffalo`) with full support for arbitrary power `n`, Julia sets, 3D lighting (2x2 real Jacobian normals), orbit traps, and cursor probing.
- Pan, zoom, and reset the view with arbitrary-depth zoom for Mandelbrot (`n = 2`), positive whole-number Multibrot (`n = 2..16`), and Julia sets.
- Set the power `n` from the slider (`-6` to `12`), type any finite number, or hold `[` and `]` to sweep it.
- Choose `1` to `100,000` iterations (logarithmic slider, text box, or `-`/`+`), or toggle **Auto iterations** to scale the limit with zoom depth.
- Inspect the point under the cursor live in the side panel: complex coordinate (`Cursor`), escape iteration / smooth value or interior magnitude (`Value`), and final complex value (`Final z`), including at deep zoom.
- Color by how fast a point escapes: cyclic bands or a histogram, with optional smoothing, density, and phase.
- Optional **3D lighting** (directional normal-map shading with a 360-degree angle slider, or **Mouse light** mode using the cursor as a live floating point light) and optional **Orbit traps** (`Point`, `Cross`, `Circle`, `Square`), working across both GPU shaders (including deep-zoom perturbation) and CPU.
- Eight palettes shared directly between CPU and GPU via a baked LUT: Classic, Fire, Electric, Ocean, Sunset, Ice, Grayscale, Rainbow.
- Paint the interior solid or with a glow based on the smallest size the orbit reached.
- Switch to a Julia set and type the constant `c`.
- Draw with tiered GPU fragment shaders (native float32, double-float, and deep-zoom perturbation with progressive tiling and idle frame caching), or switch to the CPU backend with orbit caching for instant recoloring. Histogram coloring always uses the CPU.
- Optionally evaluate positive whole-number powers with AVX2 / AVX-512 SIMD (FMA + lane refilling) on the CPU.
- Optionally show how long the newest image took to generate.
- Save the current picture as a PNG under `captures/`, or render headlessly from the command line with `--screenshot`.

## The math, simply

### A point is two numbers

A complex number is just a pair: how far left or right, and how far up or down. Adding two of them adds each part. Multiplying them both stretches and turns. That is the whole machine the fractal runs on.

### One step

Start with some `z`. Replace it by

```text
z  =  z^n  +  c
```

Do that again with the new `z`. And again.

Each step feeds the previous result back in. After enough steps, one of two things happens:

- The size of `z` stays modest forever. Call that point **inside**.
- The size of `z` grows without bound. Call that point **outside**.

"Forever" is impractical, so the program stops early. If the size passes 16, the point counts as outside. If that has not happened after the iteration limit, the point counts as inside. Raising the iteration limit rechecks the stubborn points that grow very slowly.

The color of an outside point comes from how many steps it took to pass 16. Smoothing uses the size on the escaping step so the count is not stuck on whole numbers. Histogram coloring ranks points by that count across the whole picture. The interior is either left dark, or shaded by the smallest size `z` reached along the way.

## The fractals

Same rule. The fractals differ in what is fixed and what the pixel means.

### Mandelbrot set

This is `n = 2`, so the rule is `z = z² + c`.

Every pixel is a different `c`. Everyone starts at `z = 0`.

So the picture is a map of addition constants. A pixel is inside when "start at nothing, then keep squaring and adding this pixel" never blows up. It is outside when that process eventually does.

Squaring a complex number doubles its turn and squares its length. Adding `c` then shifts the result by the pixel. Those two moves, repeated, are the whole definition.

### Multibrot sets

Change `n` and you get the same kind of map for a different power: `z = z^n + c`, still starting from `z = 0`, still using the pixel as `c`.

`n = 2` is the Mandelbrot set. `n = 3` cubes instead of squares. `n = 4` raises to the fourth. Any other whole number from `-16` to `16` is the same idea: multiply the complex number by itself that many times, then add `c`.

A whole-number power is exact multiplication. No angles are required. `n = 1` is only `z = z + c`, which walks in a straight line and almost everything escapes. `n = 0` replaces `z` by `1 + c` every step.

### Fractional powers

`n` does not have to be a whole number. `1.4` is allowed.

A complex number can be written as a length and a direction (an angle). Raising it to the power `n` raises the length to `n`, and multiplies the angle by `n`:

```text
(length, angle)  ->  (length^n, n * angle)
```

That matches whole-number powers, and it also works for fractions. `1.4` means "make the length a bit more than itself, and turn the angle by 1.4 times."

Angles are measured from the positive rightward direction, and they jump when you cross the leftward direction. That jump is a branch cut. For a fractional `n`, crossing it changes the result, because a fraction of a turn does not land on the same place from both sides. Whole-number powers do not have this seam, because a whole number of turns brings you back to the same direction.

Very deep zooms of a fractional power are less precise than whole-number powers. The GPU evaluates that polar form in ordinary single precision. Whole-number powers switch from float32 to double-float and then to arbitrary-precision perturbation so you can keep zooming far past standard floating-point limits.

### Deep zoom (perturbation)

Standard 64-bit floating-point numbers run out of precision around `1e-15`. Computing every pixel in full arbitrary-precision arithmetic would be too slow for interactive exploration.

Instead, once zoom passes `1e-12` (for integer powers `n = 2..16` and Julia sets), the viewer computes a single high-precision **reference orbit** `Z` off-thread using arbitrary-precision `BigFloat` math, then iterates only the tiny difference `dz` from that reference orbit for every pixel on the GPU or CPU. Zhuoran rebasing resets `dz` whenever a pixel drifts too far from the reference orbit to prevent glitches, and extended-exponent numbers (`FloatExp` / `ComplexExp`) keep tracking offsets well past `1e-38` and `1e-308`.

### Julia sets

Turn on Julia and the question flips.

`c` is now one fixed number for the entire picture (by default `-0.8 + 0.156i`). Each pixel is the **starting** `z`, not the thing you add.

Inside means: "if I start at this pixel and keep doing `z = z^n + c` with this same `c`, I stay finite."

You can type both parts of `c`. Changing `c` changes which starts survive. Changing `n` still changes the power inside the same rule. The Mandelbrot map and a Julia set are two readings of one formula: either the pixel chooses `c` and you start at zero, or `c` is chosen and the pixel chooses the start.

### Other fractal families

Use the **Fractal** selector (or `F` / `--fractal`) to explore four classic escape-time relatives alongside `Multibrot`, all supporting arbitrary power `n`, Julia mode, orbit traps, and 3D lighting:

- **Burning Ship**: `z = (|Re z| - i|Im z|)^n + c`
- **Tricorn (Mandelbar)**: `z = conj(z)^n + c`
- **Celtic**: `z = |Re(z^n)| + i Im(z^n) + c`
- **Buffalo**: `z = |Re(z^n)| - i|Im(z^n)| + c`

### Negative powers

A negative `n` includes a reciprocal. `z^-1` is `1/z`, and more negative integers are that idea repeated. After the power, the formula still adds `c`.

Points that pass through zero are special, because dividing by zero is undefined. Those orbits stop there.

## Run

You need the .NET 11 SDK.

```bash
dotnet run --project src/Mandelbrot.Visualizer.csproj
```

Run the headless verification suite (checks known points, SIMD parity, palette LUT endpoints, `BigFloat`/`FloatExp`/`ComplexExp` math, deep-zoom perturbation parity at `1e-25` and `1e-80`, and cursor orbit probing):

```bash
dotnet run --project src/Mandelbrot.Visualizer.csproj -- --self-test
```

You can also launch directly to a location or capture a PNG from the command line:

```bash
dotnet run --project src/Mandelbrot.Visualizer.csproj -- \
  --center -0.743643887037158704752191506114774,0.131825904205311970493132056385139 \
  --scale 1e-25 --power 2 --iter 1500 --screenshot captures/deep.png
```

Supported CLI options:

- `--self-test` - run headless math and parity checks and exit
- `--center re,im` - starting complex center (supports arbitrary decimal digits)
- `--scale value` - starting viewport half-height (supports scientific notation like `1e-45`)
- `--power n` - starting exponent `n`
- `--fractal family` - starting fractal family (`multibrot`, `burningship`, `tricorn`, `celtic`, `buffalo`)
- `--iter count` - starting max iterations (`1` to `100000`)
- `--cpu` - start with the CPU renderer instead of GPU shaders
- `--fullscreen` - launch directly in fullscreen mode
- `--lighting` / `--3d` - enable 3D normal-map lighting
- `--angle deg` - set 3D light azimuth angle in degrees (`0` to `360`, implies `--lighting`)
- `--mouse-light` - use the mouse pointer as a floating 3D point light (implies `--lighting`)
- `--light x,y` - fix the point light at normalized canvas coordinates (implies `--mouse-light`)
- `--trap mode` - enable an orbit trap (`none`, `point`, `cross`, `circle`, `square`)
- `--interior style` - set interior style (`black`, `glow`)
- `--screenshot path.png` - render a final-quality frame, save it (plus a `.window.png` capture), and exit

Pictures saved interactively from the app go to `captures/`.

## Controls

| Action | Control |
| --- | --- |
| Pan | Drag, or the arrow keys |
| Zoom | Mouse wheel. Hold Shift and Up to zoom in, Shift and Down to zoom out. Double-click zooms in. Shift double-click zooms out |
| Power `n` | Slider, text box, or hold `[` and `]`. A tap steps by 0.1 |
| Fractal | Combo box (`Multibrot`, `Burning Ship`, `Tricorn`, `Celtic`, `Buffalo`), or `F` to cycle |
| Iterations | Logarithmic slider (`1` to `100,000`), text box, or `-` and `+` |
| Auto iterations | Checkbox. Scales iteration limit automatically with zoom depth |
| Palette | Combo box, or `C` to cycle |
| Orbit trap | Combo box (`None`, `Point`, `Cross`, `Circle`, `Square`), or `T` to cycle |
| 3D lighting | Checkbox or `L`, with a 360-degree `Angle` slider |
| Mouse light | Checkbox or `M`. Uses the mouse cursor as a floating 3D point light |
| Julia set | Checkbox or `J`. `Re c` and `Im c` edit the constant |
| GPU shader | Checkbox or `G`. Histogram coloring stays on the CPU |
| SIMD | Checkbox or `V`. Speeds positive whole-number powers on the CPU with AVX2 / AVX-512. Fractional powers stay on the scalar path |
| Frame time | Checkbox. Shows how long the newest image took to generate |
| Reset view | Button or `R` |
| Hide the panel | `H` |
| Full screen | `F11` |
| Save PNG | Button or `S` |
| Quit | `Esc` |

## Architecture

The codebase is organized as vertical feature slices under `src/`:

- `src/App/` - thin Raylib window host (`VisualizerHost`) and side-panel shell (`SidePanel`)
- `src/Features/Formula/` - `FormulaParams`, scalar/SIMD Multibrot evaluation, `ReferenceOrbit`, and `PerturbationFormula`
- `src/Features/Coloring/` - palettes, smooth/histogram mapping, and GPU `PaletteLut` texture
- `src/Features/Navigation/` - arbitrary-precision `FractalView` camera, input handling, and coordinate telemetry
- `src/Features/Rendering/` - GPU tiered/tiled shaders (`multibrot_f32.frag`, `multibrot.frag`, `multibrot_perturb.frag`), CPU cached renderer, and backend dispatch
- `src/Features/Capture/` - interactive PNG saving and CLI `--screenshot` automation
- `src/Features/Diagnostics/` - cursor orbit probing, frame-time readout, HUD, and `--self-test`
- `src/Shared/` - feature-agnostic `BigFloat`, `FloatExp`/`ComplexExp`, and RayGui panel helpers
