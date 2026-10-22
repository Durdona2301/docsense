---
name: DocSense
description: A clear white-and-blue workspace for reading document evidence.
colors:
  blue: "#0862ec"
  blue-hover: "#034bc2"
  ink: "#101e35"
  muted: "#5b677c"
  line: "#e0e5ef"
  paper: "#fff"
  sidebar: "#f8f9fc"
  pale: "#f1f6ff"
typography:
  display:
    fontFamily: "Manrope, sans-serif"
    fontSize: "clamp(32px, 3.8vw, 51px)"
    fontWeight: 800
    lineHeight: 1.25
    letterSpacing: "-0.04em"
  title:
    fontFamily: "Manrope, sans-serif"
    fontSize: "21px"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "-0.03em"
  body:
    fontFamily: "Manrope, sans-serif"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.7
  reading:
    fontFamily: "Manrope, sans-serif"
    fontSize: "18px"
    fontWeight: 400
    lineHeight: 1.85
rounded:
  inset: "6px"
  control: "8px"
  source: "9px"
  panel: "10px"
spacing:
  compact: "6px"
  small: "8px"
  medium: "12px"
  regular: "16px"
  large: "20px"
components:
  button-primary:
    backgroundColor: "{colors.blue}"
    textColor: "{colors.paper}"
    rounded: "{rounded.control}"
    padding: "14px 19px"
  button-primary-hover:
    backgroundColor: "{colors.blue-hover}"
  button-secondary:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "10px 16px"
  button-secondary-hover:
    backgroundColor: "{colors.pale}"
  composer:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.panel}"
    padding: "12px 13px 12px 20px"
  answer:
    backgroundColor: "{colors.pale}"
    textColor: "{colors.ink}"
    rounded: "{rounded.panel}"
    padding: "26px 26px 22px"
---

# Design System: DocSense

## Overview

**Creative North Star: "The Clear Evidence Workspace"**

DocSense uses the user-selected conventional white-and-blue product interface: a white reading field, a cool pale-gray library, saturated blue actions and self-hosted humanist sans typography. Hairline boundaries and gently rounded controls make the workspace feel direct and familiar.

The visual emphasis belongs to questions, readable results and inspectable passages. Typography and pale tonal panels provide hierarchy; outline SVG icons support actions without competing with the text.

**Key Characteristics:**
- White reading surfaces with cool neutral support regions.
- Blue for primary actions, interaction and evidence emphasis.
- One self-hosted type family with strong headings and generous reading leading.
- Fine borders, restrained rounding and state-driven shadows.

## Colors

A single blue accent sits within a cool, quiet neutral palette; frontmatter values are normative.

### Primary
- **Action Blue** (`blue`): primary buttons, links, outline icons and interaction emphasis.
- **Pressed Blue** (`blue-hover`): the darker primary-button hover state.
- **Evidence Wash** (`pale`): answer surfaces, secondary hover states and supportive icon backgrounds.

### Neutral
- **Deep Ink** (`ink`): headings, document names and main reading text.
- **Slate** (`muted`): supporting text and secondary actions.
- **Hairline** (`line`): region boundaries and recurring container borders.
- **Reading White** (`paper`): main canvas and controls.
- **Library Gray** (`sidebar`): the library support region.

**The Action Accent Rule.** Use saturated blue to identify actions and interactive evidence; use pale blue for broad emphasis surfaces.

## Typography

**Display Font:** Manrope, sans-serif fallback.
**Body Font:** Manrope, sans-serif fallback.

**Character:** A self-hosted variable humanist sans carries the entire interface. Heavy, tightly spaced headings contrast with regular reading text and moderately weighted controls.

### Hierarchy
- **Display:** the fluid, heavy primary heading defined in frontmatter; it steps down to a fixed compact size on narrower layouts.
- **Title:** compact bold section headings with tighter tracking than body text.
- **Body:** the inherited reading and control foundation; paragraphs use relaxed leading.
- **Reading:** the answer role uses more generous leading and a measure capped at approximately 74 characters. At phone widths its implemented size is (14px); source passages remain (13px), and whole-document reading uses a similar generous measure.

Use the roles as a hierarchy, not as a requirement to give every heading or auxiliary label the same size. Keep compact metadata subordinate without treating individual metadata sizes as reusable type tokens.

## Layout

The desktop shell places a persistent library beside a flexible reading area. The usual library width is (276px), the main area is capped at (1600px), and the workspace content is capped at (1080px). The document reader narrows to (950px). Main content uses generous horizontal gutters; compact control gaps and internal padding use the recurring spacing steps in frontmatter.

At widths up to (1100px), the library and gutters narrow, question suggestions become a single column and the heading scale reduces. At widths up to (720px), the library becomes a top region, the composer stacks its full-width action below the field, and sources form one column. At widths from (1700px), the library and outer gutters expand. Source grids otherwise fit available space with a minimum card width of (230px).

Preserve text wrapping for uploaded filenames and extracted passages. Answer and source content must remain in the same reading flow, with citation activation expanding and focusing the relevant passage.

## Elevation & Depth

Surfaces are flat at rest. Pale fills and fine borders distinguish library, composer, answer and source regions. Small blue-tinted shadows appear only around the active composer and a focused source; depth communicates interaction rather than decoration.

### Shadow Vocabulary
- **Composer focus:** a shallow blue wash (`0 3px 16px #0862ec12`) paired with a blue border.
- **Source focus:** a shallow blue wash (`0 4px 15px #0862ec13`) paired with a blue border.

**The State Depth Rule.** Keep ordinary surfaces flat; reserve the observed soft shadows for input focus and evidence focus.

## Shapes

Controls use softly rounded rectangular silhouettes. The shared panel radius is slightly larger than the control radius, with smaller rounding inside passage insets. Thin borders carry structure. Circular answer icons and larger rounded empty-state icons are supporting accents, rather than the default shape for containers.

## Components

### Buttons

Clear and moderately weighted. Primary actions use blue with white text; secondary actions use white with a light border and dark text. Hover darkens primary blue or adds a pale wash to secondary surfaces. Primary controls are typically at least (48px) high and secondary controls at least (44px) high. Inline text actions use muted text that turns blue on hover. Disabled actions reduce opacity and use an unavailable or waiting cursor appropriate to state.

### Inputs / Fields

The question composer is a white, hairline-bordered rounded container around a resizable textarea and action. Focus within changes its border and adds the composer shadow. Textareas use blue carets; placeholders are muted. Preserve the field's accessible label even when it is visually hidden.

### Chips

Mode badges use a pale neutral fill and a small outline icon with moderately weighted text. Citation buttons use pale blue with dark blue text, gain a stronger wash on hover, and take the reader to an expanded, focused source.

### Cards / Containers

The answer uses a pale-blue panel. Source cards use a near-white tinted surface and a thin neutral border, with original text inset on white. Source focus changes border and shadow; long passages can expand without leaving the page. Do not hide evidence behind decorative interactions.

### Navigation

The library uses text-first document rows with outline file icons. Selected rows receive a pale-blue background. Names wrap; hovered names turn blue. On phones the library becomes a bounded, scrollable top grid rather than a fixed side rail.

### Interaction States

Keyboard focus uses a visible blue outline (3px, offset 4px), except inside the composer where the entire container receives focus treatment. Buttons transition background and color over (0.16s, ease-out). Answer arrival uses a short reveal (0.35s). Reduced-motion preference disables animation, transitions and smooth evidence scrolling. Loading and errors use visible status text and a restrained warm alert surface.

## Do's and Don'ts

### Do:
- **Do** use self-hosted Manrope for display and body text.
- **Do** use pale surfaces and fine borders to structure reading regions.
- **Do** keep citation focus visible and preserve readable, wrapping source text.
- **Do** collapse the workspace into a coherent single-column reading flow on phones.
- **Do** retain visible keyboard focus and respect reduced motion.

### Don't:
- **Don't** apply persistent decorative shadows to ordinary cards.
- **Don't** use saturated blue as the general reading background.
- **Don't** substitute glyph characters for the implemented outline SVG icons.
- **Don't** promote isolated metadata sizes into the shared typography scale.
