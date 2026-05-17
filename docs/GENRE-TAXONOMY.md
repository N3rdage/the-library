# Genre taxonomy

The curated genre tree used by BookTracker, plus where each branch came from.

For the field-level details of the `Genre` entity (uniqueness, hierarchy mechanics, selection auto-cascading) see [`DATA-DICTIONARY.md`](DATA-DICTIONARY.md) §Genre.

---

## Source of truth

The seed list lives in [`BookTracker.Data/Models/GenreSeed.cs`](../BookTracker.Data/Models/GenreSeed.cs) as a static `IReadOnlyList<Entry>` of `(Name, ParentName)` records. Each migration that extends the taxonomy uses this list to insert new rows — the file is the canonical taxonomy, the DB is the runtime reflection of it.

Branch reference: [fictionary.co/journal/book-genres](https://fictionary.co/journal/book-genres/) — the structural inspiration for the fiction branches.

---

## Current tree

### Fiction branches

```
Fantasy
├── High (Epic) Fantasy
├── Urban (Contemporary) Fantasy
├── Dark Fantasy
├── Grimdark
├── Sword and Sorcery
├── Portal Fantasy
├── Fairy-Tale Retelling
├── Mythic Fantasy
├── Steampunk Fantasy
├── Paranormal Fantasy
├── Historical Fantasy
└── Young Adult Fantasy

Romance
├── Contemporary Romance
├── Historical Romance
├── Paranormal Romance
├── Romantic Suspense
├── Regency Romance
├── Dark Romance
├── Last Chance Romance
├── Enemies to Lovers
├── Fake Dating
├── Young Adult Romance
└── Romantasy

Mystery
├── Cozy Mystery
├── Hard-Boiled / Detective
├── Police Procedural
├── Noir
├── Historical Mystery
├── Heist Mystery
├── Whodunit
├── Legal Thriller
└── Private Detective

Horror
├── Cthulhu Mythos
├── Vampire
├── Zombie
├── Cosmic Horror
├── Gothic Horror
├── Supernatural / Ghost Story
├── Psychological Horror
├── Splatterpunk
├── Folk Horror
└── Body Horror

Science Fiction
├── Hard SF
├── Space Opera
├── Cyberpunk
├── Military SF
├── Time Travel
├── First Contact
├── Post-Apocalyptic
├── Dystopian SF
├── Alternate History
└── Young Adult SF

Historical Fiction           (top-level, no sub-genres yet)
Thriller                     (top-level, no sub-genres yet)
Adventure                    (top-level, no sub-genres yet)
Literary Fiction             (top-level, no sub-genres yet)
Coming-of-Age                (top-level, no sub-genres yet)
Satire                       (top-level, no sub-genres yet)
Dystopian                    (top-level, no sub-genres yet — kept separate from Dystopian SF for non-SF dystopias)
Utopian                      (top-level, no sub-genres yet)
Magical Realism              (top-level, no sub-genres yet)
Biographical Fiction         (top-level, no sub-genres yet)
Western                      (top-level, no sub-genres yet)
Graphic Novels               (top-level, no sub-genres yet)
Short Story Collections      (top-level, no sub-genres yet)
```

### Non-fiction branches

```
Reference
├── Dictionaries
├── Encyclopedias
├── Atlases
├── Field Guides
├── Style Guides
└── Language Learning

Art
├── Art History
├── Artist Monographs
├── Art Theory
├── Photography
├── Architecture
└── Design

Religion & Spirituality
├── Sacred Texts
├── Biblical Studies
├── Theology
├── Comparative Religion
└── Mythology
```

---

## Provenance

| Branch | Added by | Migration |
|--------|----------|-----------|
| Fantasy + Romance + Mystery sub-trees + most top-level fiction | Initial seed | `20260414111237_AddGenreHierarchyAndSeed` |
| Horror sub-genres (Cthulhu Mythos / Vampire / Zombie) | Follow-up seed | `20260419110101_SeedHorrorSubGenres` |
| Reference + Art + Religion & Spirituality (all three branches and their sub-genres) | Non-fiction starter set | `20260422090308_SeedNonFictionReferenceArtReligion` |
| Science Fiction sub-tree (Hard SF / Space Opera / Cyberpunk / Military SF / Time Travel / First Contact / Post-Apocalyptic / Dystopian SF / Alternate History / Young Adult SF) + Horror sub-tree extension (Cosmic Horror / Gothic Horror / Supernatural / Psychological Horror / Splatterpunk / Folk Horror / Body Horror) | Genre-restructure pass (PR 1) | `20260516233131_SeedGenreExpansion` |

User-added genres (created via the Add page typeahead's free-text fall-through, if that surface allows it — currently the picker is closed-list against `GenreSeed.All`) would *not* be in `GenreSeed.cs` but would live in the DB. If you see a Genre row in the snapshot that's not in this file, it's either a stale row from a withdrawn seed, an out-of-band insert, or this file is behind. As of this writing, the picker is closed-list, so the DB and `GenreSeed.cs` should match exactly.

---

## Branches *not* yet seeded

The original comment in `GenreSeed.cs` flags non-fiction expansion as deliberate future work — branches that are missing today and would be the obvious next additions:

- **History** (Ancient / Medieval / Modern / Military / Local-regional)
- **Biography** (Memoir / Autobiography / Authorised / Unauthorised)
- **Science** (Popular / Mathematics / Physics / Biology / Earth-science / etc.)
- **Poetry**
- **Cookery / Cookbooks** (currently no home — cookbooks tagged today go under Reference or unclassified)
- **Travel writing**
- **Essays / Letters**
- **Self-help / Productivity**
- **Politics / Current affairs**

If a brainstorming session proposes adding a branch, the addition is:
1. New `new("Branch Name", null)` (or with parent) entries in `GenreSeed.cs`.
2. A new EF migration following the pattern of `SeedNonFictionReferenceArtReligion.cs` — `INSERT` rows into `Genres` with explicit IDs continuing from the last seeded row.
3. No code change in the picker — it loads from `Genres` at runtime.

---

## Conventions worth remembering for restructuring discussions

- **Selecting a sub-genre auto-selects its parent** on the Add page. Aggregations should respect this — a Work tagged "Urban Fantasy" *is* in the Fantasy bucket whether or not Fantasy was also explicitly selected.
- **Genre name uniqueness is global**, not scoped to a parent. You can't have "Romance > Historical" and "Mystery > Historical" — name it "Historical Romance" / "Historical Mystery" instead (as the current tree does).
- **Hierarchy is single-parent** — a sub-genre belongs to exactly one parent. "Romantasy" is parented under Romance, not also under Fantasy, even though it sits on the boundary.
- **Single-Work books inherit Genre via their Work.** A novel is "Fantasy" because the Work is; a compendium's Book-level summary has to aggregate Genres across all contained Works.
- **`BookCategory` (`Fiction` / `NonFiction`) is orthogonal to genre** — *Biographical Fiction* is `Category = Fiction` but genre-tagged on the Work. There's no enforced consistency check between `BookCategory` and Genre parentage today.

---

## Open questions worth flagging in restructuring brainstorms

1. **Where do cookbooks go?** Currently nowhere clean — Reference subtree was scoped to dictionaries / encyclopaedias / atlases / field guides / style guides / language learning. A new top-level "Cookery" or sub-branch under Reference would resolve it. Surfaced because reference book capture is the next planned wave (TODO #51).
2. ~~**Sci-fi has no sub-genres yet** despite being one of the largest fiction branches.~~ **Resolved 2026-05-17** by `SeedGenreExpansion` — 10 sub-genres added including `Dystopian SF`. The existing top-level `Dystopian` is kept for non-SF dystopias (literary, near-future thriller) per the genre-restructure brief.
3. **`Short Story Collections` as a top-level genre is structurally odd** — it's a *format* indicator, not a genre. A Christie short-story collection has Genre = Mystery on its constituent Works, plus the volume might (or might not) carry the Short-Story-Collections tag. Worth deciding whether to keep it, or migrate volumes that use it to a Book-level Tag instead.
4. **`Graphic Novels` similar concern** — format or genre? Currently treated as genre.
5. **`Romantasy` vs `Paranormal Fantasy` vs `Paranormal Romance`** — three sub-genres at the boundary that may overlap in practice. Worth a usage scan: are all three actively used? Do any captures land in two of them simultaneously?
