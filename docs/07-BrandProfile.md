# BrandProfile

## Purpose

BrandProfile represents the visual and historical identity of a specific brand.

It defines how a brand appeared during a particular historical period.

BrandProfile does not describe a physical location.

BrandProfile provides brand-specific historical characteristics that may be applied to any Scene.

---

# Philosophy

SceneDNA defines physical reality.

EraProfile defines historical reality.

BrandProfile defines commercial identity.

Together, these objects allow historically accurate reconstruction.

---

# Examples

Examples of brands include:

* Shell
* Texaco
* Mobil
* Gulf
* Exxon
* Sears
* Kmart
* Blockbuster
* McDonald's
* Pepsi
* Coca-Cola

---

# What BrandProfile Contains

BrandProfile stores brand-specific characteristics.

Examples include:

* logos
* typography
* colors
* signage
* architecture
* canopies
* fuel pumps
* uniforms
* advertisements
* packaging
* store interiors
* lighting
* branding materials

---

# Logical Structure

```text
BrandProfile

Identity
    Name
    Logo
    Colors

Architecture
    Buildings
    Canopies
    Materials

Signage
    Signs
    Typography
    Billboards

Products
    Packaging
    Displays

Personnel
    Uniforms

Marketing
    Advertising
    Promotional Materials
```

---

# Relationship to SceneDNA

BrandProfile never changes the physical structure of the Scene.

BrandProfile only affects brand-specific visual elements.

---

# Relationship to EraProfile

BrandProfile depends on historical context.

The same brand may appear differently in different periods.

Examples:

* Texaco 1955
* Texaco 1975
* Texaco 1995

---

# Relationship to Prompt

BrandProfile is an optional input for prompt generation.

Prompt may combine:

* SceneDNA
* EraProfile
* BrandProfile
* Knowledge
* Style

---

# Design Principles

* BrandProfile contains no geometry.
* BrandProfile contains no prompts.
* BrandProfile contains no generated assets.
* BrandProfile is independent of AI providers.
* Multiple Scenes may reference the same BrandProfile.
* Multiple Eras may reference the same BrandProfile.

---

# Summary

BrandProfile represents the historical identity of a brand.

It separates commercial identity from physical and historical reality.

This separation enables accurate reconstruction of branded environments across multiple historical periods.