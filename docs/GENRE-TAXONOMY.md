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

Performing Arts
├── Stage Plays
└── Screenplays & TV Scripts
```

> **Performing Arts is on the fiction side** — a play or screenplay is a creative work in the same sense as a novel. Shakespeare, Aristophanes, sitcom scripts, comedy stand-up scripts all land here. Books *about* productions (making-of, behind-the-scenes, theatre history / criticism) go under **Media Studies** on the non-fiction side, not here.

> Graphic novels and short-story collections are format indicators, not genres: they are `format:graphic-novel` / `format:short-stories` Tags on the Book, and the constituent Works keep their thematic genre (e.g. Mystery for a Christie collection). See [`DATA-DICTIONARY.md`](DATA-DICTIONARY.md) §Tag conventions.

### Non-fiction branches

```
Reference
├── Dictionaries
├── Encyclopedias
├── Atlases
├── Field Guides
├── Style Guides
├── Language Learning
├── Philosophy
├── Cookery
├── Travel Guides
└── How-to & Instruction

Art
├── Art History
├── Artist Monographs
├── Art Theory
├── Photography
├── Architecture
├── Design
└── Music

Religion & Spirituality
├── Sacred Texts
├── Biblical Studies
├── Theology
├── Comparative Religion
└── Mythology

History
├── Ancient History
├── Medieval History
├── Modern History
├── Military History
├── Local & Regional History
├── Social & Cultural History
└── Popular History

Biography
├── Memoir
├── Autobiography
├── Authorised Biography
├── Unauthorised Biography
└── Letters & Diaries

Science
├── Popular Science
├── Mathematics
├── Physics & Astronomy
├── Biology & Natural History
├── Earth & Environmental Science
├── Medicine & Anatomy
└── Computer Science

Psychology & Self-help
├── Cognitive Science
├── Clinical & Therapeutic
├── Social Psychology
├── Self-help & Productivity
└── Philosophy of Mind

Poetry                       (top-level, no sub-genres yet)
Travel Writing               (top-level, no sub-genres yet — distinct from Reference → Travel Guides; this is Bryson-style narrative, not Lonely-Planet practical)
Politics & Current Affairs   (top-level, no sub-genres yet)
Media Studies                (top-level, no sub-genres yet)
```

> **Memoir** lives under Biography and **Popular Science** under Science. Both rows were first seeded under Reference and re-parented by `SeedNonFictionExpansion` with their Genre row IDs unchanged (see Provenance).

---

## Provenance

| Branch | Added by | Migration |
|--------|----------|-----------|
| Fantasy + Romance + Mystery sub-trees + most top-level fiction | Initial seed | `20260414111237_AddGenreHierarchyAndSeed` |
| Horror sub-genres (Cthulhu Mythos / Vampire / Zombie) | Follow-up seed | `20260419110101_SeedHorrorSubGenres` |
| Reference + Art + Religion & Spirituality (all three branches and their sub-genres) | Non-fiction starter set | `20260422090308_SeedNonFictionReferenceArtReligion` |
| Science Fiction sub-tree (Hard SF / Space Opera / Cyberpunk / Military SF / Time Travel / First Contact / Post-Apocalyptic / Dystopian SF / Alternate History / Young Adult SF) + Horror sub-tree extension (Cosmic Horror / Gothic Horror / Supernatural / Psychological Horror / Splatterpunk / Folk Horror / Body Horror) | Genre-restructure pass | `20260516233131_SeedGenreExpansion` |
| `Graphic Novels` + `Short Story Collections` removed from the tree (moved to `format:*` Tag convention); `format:graphic-novel` + `format:short-stories` Tag rows seeded | Genre-restructure pass | `20260517041346_RemoveFormatGenres` |
| Reference sub-genres (Popular Science / Memoir / Philosophy) + top-level Poetry | Genre-restructure round 2 | `20260522023150_SeedNonFictionSubGenresAndPoetry` |
| Non-fiction expansion — History / Biography / Science / Psychology & Self-help / Travel Writing / Politics & Current Affairs / Media Studies sub-trees, plus Performing Arts (fiction-side), plus Cookery / Travel Guides / How-to & Instruction (under Reference), plus Music (under Art); re-parent of Memoir under Biography and Popular Science under Science; five `format:*` Tags (`reference` / `notebook` / `script` / `textbook` / `illustrated`) | Non-fiction expansion | `20260522052911_SeedNonFictionExpansion` |

User-added genres (created via the Add page typeahead's free-text fall-through, if that surface allows it — currently the picker is closed-list against `GenreSeed.All`) would *not* be in `GenreSeed.cs` but would live in the DB. If you see a Genre row in the snapshot that's not in this file, it's either a stale row from a withdrawn seed, an out-of-band insert, or this file is behind. The picker is closed-list, so the DB and `GenreSeed.cs` should match exactly.

---

## Branches *not* yet seeded

- **Essays / Letters** — *Letters & Diaries* under Biography covers the *letters* half. A standalone *Essays* branch is deferred — no clear separation from Literary Fiction without a corpus signal.
- **Institutional authorship** — Encyclopaedia Britannica, OUP, etc. as Author. Separate concern from this taxonomy work.

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
- **`BookCategory` (`Fiction` / `NonFiction`) is orthogonal to genre** — *Biographical Fiction* is `Category = Fiction` but genre-tagged on the Work. There's no enforced consistency check between `BookCategory` and Genre parentage.
- **Top-level `Dystopian` is for non-SF dystopias** (literary, near-future thriller); SF dystopias take `Science Fiction → Dystopian SF`.

---

## Open questions worth flagging in restructuring brainstorms

1. **`Romantasy` vs `Paranormal Fantasy` vs `Paranormal Romance`** — three sub-genres at the boundary that may overlap in practice. Worth a usage scan: are all three actively used? Do any captures land in two of them simultaneously?
