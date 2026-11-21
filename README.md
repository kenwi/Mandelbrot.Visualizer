# Mandelbrot.Visualizer

An interactive viewer for the Mandelbrot set and its relatives. Every pixel asks the same question: if you follow the rule `z = z^n + c` over and over, do the numbers stay finite or run off to infinity?

`n` can be any real number, not only 2. The panel edits the power, the iteration limit, the coloring, and whether you are looking at a Mandelbrot-style map or a Julia set. The picture can be drawn on the CPU or with a GPU shader.

## Capabilities

- Pan, zoom, and reset the view. Zoom reaches about `1e-14` of the starting scale.
- Set the power `n` from the slider (`-6` to `12`), type any finite number, or hold `[` and `]` to sweep it.
- Choose 16 to 4000 iterations. More iterations decide borderline points more carefully.
- Color by how fast a point escapes: cyclic bands or a histogram, with optional smoothing, density, and phase.
- Eight palettes: Classic, Fire, Electric, Ocean, Sunset, Ice, Grayscale, Rainbow.
- Paint the interior solid or with a glow based on the smallest size the orbit reached.
- Switch to a Julia set and type the constant `c`.
- Draw with a GPU fragment shader, or fall back to the CPU. Histogram coloring always uses the CPU.
- Save the current picture as a PNG under `captures/`.

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

Very deep zooms of a fractional power are less precise than whole-number powers. The GPU evaluates that polar form in ordinary single precision. Whole-number powers on the GPU use a double-float iteration so the zoom can go much deeper before the pixels fall apart.

### Julia sets

Turn on Julia and the question flips.

`c` is now one fixed number for the entire picture (by default `-0.8 + 0.156i`). Each pixel is the **starting** `z`, not the thing you add.

Inside means: "if I start at this pixel and keep doing `z = z^n + c` with this same `c`, I stay finite."

You can type both parts of `c`. Changing `c` changes which starts survive. Changing `n` still changes the power inside the same rule. The Mandelbrot map and a Julia set are two readings of one formula: either the pixel chooses `c` and you start at zero, or `c` is chosen and the pixel chooses the start.

### Negative powers

A negative `n` includes a reciprocal. `z^-1` is `1/z`, and more negative integers are that idea repeated. After the power, the formula still adds `c`.

Points that pass through zero are special, because dividing by zero is undefined. Those orbits stop there.

## Run

You need the .NET 11 SDK.

```bash
dotnet run --project src/Mandelbrot.Visualizer.csproj
```

`dotnet run --project src/Mandelbrot.Visualizer.csproj -- --self-test` checks a few known inside and outside points and does not open a window.

Pictures saved from the app go to `captures/`.

## Controls

| Action | Control |
| --- | --- |
| Pan | Drag, or the arrow keys |
| Zoom | Mouse wheel. Double-click zooms in. Shift double-click zooms out |
| Power `n` | Slider, text box, or hold `[` and `]`. A tap steps by 0.1 |
| Iterations | Slider, text box, or `-` and `+` |
| Palette | Combo box, or `C` to cycle |
| Julia set | Checkbox or `J`. `Re c` and `Im c` edit the constant |
| GPU shader | Checkbox or `G`. Histogram coloring stays on the CPU |
| Reset view | Button or `R` |
| Hide the panel | `H` |
| Full screen | `F11` |
| Save PNG | Button or `S` |
| Quit | `Esc` |
