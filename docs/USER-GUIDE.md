# Bookcase user guide

Bookcase is a web app for the books you own. You record each book once, then search it, sort it, track what you have read, lend it out and see what is missing from your series. Bookshelf is its Android companion: it carries a copy of your library in your pocket, so you can check a book in a shop without a signal.

## Key concepts

**Book.** The physical thing on your shelf: one volume. A Book carries your reading state (status, rating, notes), its cover, its tags, and its place in a series. Status is one of Unread, Reading, Read or Reference. Rating is 0 to 5 stars.

**Work.** A story, novel or play, apart from any volume it is printed in. Most Books hold one Work. A compendium holds several, and the same Work can sit inside more than one Book. Authors, subtitle, first-published date and genres belong to the Work.

**Edition.** One printing of a Book: its ISBN, format (Hardcover, Trade paperback, Mass market paperback or Large print), publisher, print date and optional cover. A Book can have several Editions. An ISBN can belong to only one Edition. Leave it blank for old or self-published printings.

**Copy.** One physical item of an Edition. If you own two of the same printing, that is one Edition with two Copies. A Copy has a condition (As New, Fine, Very Good, Good, Fair or Poor), an acquired date and notes.

**Series and Collection.** A Series is a numbered run with a known order, such as a trilogy. A Collection is a looser grouping with no fixed order. You attach a Book to either one, and give it a position in the Series. A Series can have an expected number of books, which is what lets Bookcase tell you what is missing.

**Author and pen name (alias).** Each Work points at an Author row. A pen name is its own Author row that you link to the real author as an alias. Totals and searches then roll the pen name up into the real author.

**Genre.** A label for a Work, such as Fantasy. Genres have parents and children. Picking a child, such as Cthulhu Mythos, also picks its parent, Horror.

**Tag.** A free label on a Book, such as `follow-up`. Tags are flat and you make them up as you go.

**Wishlist.** Books you want but do not own. Each entry has a title, author, priority (Low, Medium or High) and an optional ISBN and series place.

**Loan.** A record that you lent a Copy to someone. It has a borrower's name, the date lent, an optional due date, and a returned date once it is back. A Copy can be on loan to only one person at a time. A Book with a Copy on loan cannot be deleted.

## Navigation

The top bar lists, in order: Home, Library, Add book, Bulk add, Series, Shelf order, Authors, Publishers, Wishlist, On loan, Bookshop, AI Assistant, Duplicates, Privacy, Manual and Help. On a narrow screen the bar collapses into a menu button. The brand name Bookcase links back to Home.

## Home

Home is a summary of your library.

- Two tiles show **Books in collection** and **Unique authors**. Click a tile to open the Library or the Authors page.
- **Due back** appears only when something is lent out. It lists loans that have a due date, soonest first, as "title, borrower, due date". An **Overdue** chip marks late ones. The calendar icon on a row downloads a calendar event for that return. A line such as "2 more out, no return date" counts open-ended loans. **See all** opens the On loan page.
- **Top 10 authors** shows bars by book count. Click an author to open their page. Pen names count toward the real author.
- **Top 10 genres** shows bars by book count.

## Library

The Library lists every book. Use it to find a book, change its status or rating, and open its page.

### Filtering

- **Search** matches title or author. Type your words and press Enter.
- **Group by** is Author, Genre or "None (flat list)".
- **Category** is All, Fiction or Non-Fiction.
- **Genre** includes "(no genre)". **Series** includes "(no series)". **Tag**, **Status** and **Author** narrow the list further.
- The clear button (the X) resets every filter. On a phone, the **Filters** button shows or hides the secondary filters, and **Clear** resets them.

The filters live in the page address. If you open a book and press Back, you return to the same filtered list.

### Groups and the flat list

With Group by set to Author or Genre, you see one row per group with a book count. Click a group to open a flat list filtered to it. Press Back to return to the groups. With "None (flat list)" you see the books directly, with page numbers below and a line such as "Showing 1-50 of 400 books".

### Working on a row

Each row shows a cover, title, author, genres, tags, status and rating.

- Click the **cover** to open the book. The title is plain text, so you can select and copy it.
- The **status chip** is a menu. Click it and pick Unread, Reading, Read or Reference. The change saves at once.
- Picking **Read** opens a **Mark as Read** dialog. Set a rating and optional notes, then press **Mark Read**. The status, rating and notes save together. **Cancel** leaves the book as it was.
- Click the **stars** to set the rating.
- A **Lent to** chip appears on a book that is out on loan.
- A book with more than one Work shows a count such as "3 works".

A changed row stays in place until the next reload, even if it no longer matches the Status filter.

## Add book

Add book records one book with its first edition and copy. Fill in what you know and press **Save book**. After saving, you go to Home. Tick **Add another?** first to stay on the page with a blank form. The **?** button beside the page title opens a short help note.

### Finding the book

- **Look up by ISBN** and **Look up** search Open Library, then Google Books, and fill the form. You can edit anything before saving. Press Enter in the box to look up.
- The switch **No ISBN (pre-1974 book)** swaps the ISBN box for **Title** and **Author** boxes and a **Search** button. Search lists candidates from Open Library. Press **Use this** on one to fill the form. You still need to enter the format, print date and publisher from the book in your hand.
- **Fill from a photo** needs an AI provider and is hidden in collection mode. Press **Take or choose photo** and photograph the cover or title page. The AI reads the title, authors, publisher, year and ISBN. If it read an ISBN, that is looked up; if the lookup finds nothing, or there was no ISBN, Open Library is searched by title and author. The photo and the matches appear together (stacked on a phone). **Use this** on a match fills the form. **None of these** fills it from what the photo said, leaving out anything the AI was unsure of. Nothing is saved until you press **Save book**.
- When the match has no cover, or you chose **None of these**, the photo is held as the cover in the **Cover photo** card; remove or replace it there. If the AI judges the book a reference work, Status is set to Reference and a Reference genre (such as Dictionaries or Atlases) is picked; change either before saving.
- If you already own the ISBN, a warning says "You already own this book" and shows how many copies you have. **Add another copy** adds a Copy to the existing Book and opens that book's page (with **Add another?** ticked it stays on the Add page instead). **View existing book** opens it. A fresh lookup clears an old refusal message.
- If the lookup suggests a series, a **Series suggestion** banner appears. **Use this** fills the Series field and the position. Close it with the X to ignore it.

### The form

- **Book** card: Title, Category, Status, Rating (0-5), Default cover URL and Notes. A **Cover preview** sits beside it. A **Cover photo** card, under it, takes a photo with the camera or from your files; drag the box over the cover and its corner to resize it, then press **Use this crop**. When you save, the photo becomes the cover and replaces any cover URLs. If only the photo fails to upload, the book is still saved and a message says so. The card says so when cover storage is not set up.
- **Work** card: Title, Author(s), Subtitle, First published and Other contributors (editor, translator, illustrator). Leave Author(s) blank for edited works such as dictionaries. Type an author and press Enter or comma to add them. Author names are matched to existing authors; a name you have not used before creates an author.
- **Genres** card: type to find a genre and pick it. After a lookup, subjects that match your genre list are picked for you, and the other subjects are listed as hints.
- **Series / collection** card: type to find an existing series. If there is no exact match, choose the **Add "..."** row to create one. Nothing is created until you choose. Once a series is chosen, an **Order / position** box appears.
- **First edition & copy** card: ISBN, Format, Date printed, Edition #, Condition, Publisher and Edition cover URL. Publisher works like Series: pick one or choose the **Add "..."** row. A year alone is fine for Date printed.

### Collection mode

Switch on **This is a collection (multiple works in one book)** for a compendium. The single Work card and Genres card are replaced by **Works in this collection**.

- Each row is one Work with its own Title, Author(s), contributors, Subtitle, First published and genres. Press **+ Add another work** for another row.
- Start typing a title. If a matching Work already exists in your library, it appears in the list. Pick it to attach that Work. The row turns into a summary marked **Existing work**. Press **Edit as new** on it to turn it back into a blank row that keeps the title you typed.
- Press Enter on free text to move to the next row, adding one at the end of the list. **Remove** deletes a row once there is more than one.
- **Same author(s) for all works** and **Same genre(s) for all works** show one shared picker and hide the per-row ones.
- Switching collection mode off while rows hold existing Works asks "Discard attached works?" first. **Discard** drops the attachments. **Keep collection mode** cancels.

## Bulk add

Bulk add is for capturing a pile of books by ISBN. It looks each ISBN up and gives you a grid to confirm. The **?** button beside the page title opens a short help note.

### Getting ISBNs in

- Type or paste an ISBN in **Add ISBN** and press Enter or **Add**.
- **Scan barcode** opens the camera. Point it at the barcode. **Stop scanner** closes it.
- **Photo ISBN** opens the camera for a photo. Frame the printed number and press **Capture**. An AI provider reads the number and adds it. The camera stays open for the next book. **Cancel photo** closes it.
- The provider picker and its call count chip choose which AI provider reads photos and suggests genres.

### Genres and the AI switch

The **Ask AI for genres** switch is off by default and is not saved. With it off, genres come only from the lookup. With it on, a row whose lookup found no genre, or only top-level genres, asks the AI provider for more. Genres the AI suggested carry a small **AI** mark so you can spot a wrong one before accepting. The switch is greyed out when no AI provider is set up.

### The grid

Each row shows the ISBN, title, author, source, and a status chip: **Searching...**, **Found**, **Not found** or **In library**. You can edit the author on any row. Use commas or "&" between co-authors. On a **Not found** row you can also type the title. A row may show a **Series** suggestion with **Accept**, and after accepting, **Undo**.

### Accept and follow-up

- **Accept** saves the row as a Book with one Work, Edition and Copy. It is saved as Fiction, Unread, in Good condition, and as a trade paperback unless the lookup knew the format. The row then leaves the grid.
- **Follow-up** saves it the same way and adds the tag `follow-up`, so you can find it later in the Library with the Tag filter and check it by hand. On a **Not found** row the button reads **Add as follow-up**. A blank title becomes "Unknown book" followed by the ISBN.
- **Accept all** (above the grid) accepts every **Found** row that is not already in your library. It stops at the first refusal.
- A row for an ISBN you own shows **Add copy** and **Skip**. **Add copy** adds a Copy to the existing Book. **Skip** marks the row Duplicate and does nothing else.
- The X on a row removes it from the grid without saving.
- **Back to home** leaves the page.

## Series

The Series page lists every Series and Collection. Search by name or author, filter by **Type**, and use the X to clear. **New series** opens an empty form. Each row shows the type and a progress badge: "3 / 7" when a Series has an expected count, or "3 books" otherwise. The badge is green when you have them all.

Click a row to edit it. On the edit page, the **?** button beside the title opens a short help note about book positions.

- **Details**: Name, Author, Type, **Expected books** (Series only) and Description. The author here is only a display label.
- **Books in this series**: the **#** box on each row is the position. Type a whole number, or a label such as "4.5" for a book that sits between 4 and 5, or "1A". Books sort by the leading whole number, and the label you typed is what you see. Numbers below 1 are ignored. Position labels also show on the Shelf order, Wishlist and Authors pages.
- **Add book** opens a search by title or author. **Add** attaches a result. A result already in another series is marked **Already in a series**, and adding it moves it.
- **Remove** takes a book out of the series without deleting it.
- **Create series** or **Save changes** saves. **Delete series** asks "Delete this series?" and then **Yes, delete**.

## Shelf order

Shelf order is a plain list to read at the bookcase or print. Each book sits under the top-level genre of its first Work. A book with several top-level genres sits under the first alphabetically and is marked "also" with the others. Within a genre the order is author surname, then series and number, then first-published year, then title. Click a title to open the book.

## Authors

Authors lists every author with work, book and series counts, and a chip: **canonical** or **alias of** a name.

- **Search** matches author names and pen names, so typing a pen name finds the real author.
- **Show alias rows** is off by default. Switch it on to list pen names as their own rows.
- Click a row to open the author.

### Alias resolution

When you type an author name on any form, Bookcase finds the author with exactly that name, or creates one. It does not guess that two names are the same person. To join them, use the author's page.

On an author's page:

- **Rename** changes the name. Press **Save** or **Cancel**.
- On a canonical author, **Mark as alias of...** picks the real author this name is a pen name of. The Works stay attached to the pen name, but counts and the Home top-authors chart roll up under the real author. The real author's page lists the aliases it includes.
- On an alias, **Promote to canonical** makes it independent again.
- The **Works** and **Books** chips switch the list. Works show their series, first-published date, and an "as" chip with the pen name when written under one. Books show edition and copy counts.

To fix a spelling duplicate, such as "Doug Preston" and "Douglas Preston", use [Duplicates](#duplicates).

## Publishers

Publishers lists every publisher that is attached to an edition, with edition counts. Click a row to expand it and see its editions. Click an edition to open its book.

- **Rename** fixes a typo.
- **Merge into...** finds another publisher. A confirm line says how many editions move and that the source is deleted. **Confirm merge** does it. **Cancel** backs out. A merge cannot be undone.
- **Delete** appears only on a publisher with no editions.

## Wishlist

The Wishlist page has four cards.

**Find a book to add.** Type an ISBN, or words from a title or author, and press **Search** or Enter. It searches online and shows up to ten candidates with cover, ISBNs and source. A candidate may carry **Already in your library** (with a View link) or **Already on your wishlist**. **Add** puts it on the wishlist. **Advanced search** gives separate **Title**, **Author** and **ISBN** boxes. A filled ISBN wins. **Hide advanced** returns to the single box. **Clear** empties the results.

**Series gaps.** Press **Check gaps**. It lists numbered series where you own some but not all, with a badge such as "3 / 7" and the missing positions. Click positions to select them, or press **Select all**, then **Add N to wishlist**. These are added as placeholder entries with just the series and position. **Clear** drops the selection. "You have N books" expands to your owned books in that series.

**Open-ended series.** Shown after you check gaps. It lists numbered series with no expected total. Set how many of the next positions to add (the box starts at 10), then press **Add to wishlist**. Positions you own are skipped.

**Wishlist.** Press **Load list** to see your entries, with cover, title, author and a priority badge. **Quick add** opens boxes for Title, Author, ISBN (optional) and priority, for books the search cannot find. On each entry:

- **Bought** adds the book to your library with the `follow-up` tag, removes it from the wishlist and opens the book.
- **Remove** deletes the entry.

## On loan

On loan shows what you have lent out, grouped by borrower. Each loan shows the book, who has it, "since" the date, and either the due date or "no return date". An **Overdue** chip marks late loans. Under the list, **History** expands to returned loans.

- **Lend a book** opens the **Lend** dialog.
- **Returned** on a loan marks it back today.
- The calendar icon on a loan with a due date downloads a calendar event for the return. **Add all to calendar** on a borrower with due dates downloads one file for all of theirs.

### Lending a copy

The **Lend** dialog has:

- **Lent to**: the borrower's name. It suggests names you have used before.
- **Lent on**: the date, which you can change.
- **Due back (optional)**: a date, or leave it empty for no return date.
- **Find a book** (then **Add another book**): type at least two letters of a title and pick a free copy. Copies already on loan are left out. Add several books to lend them all to one person. The X on a copy removes it from the list.
- **Lend**, or "Lend N copies", saves. **Cancel** closes.

On a book's own page, the header shows **Lend** when the book has a free copy. It opens the same dialog with that copy listed. Each copy on the page also has its own **Lend** button. When every copy is out, the header shows "Lent to" and the borrower names, with a **Returned** button that marks all of them back. A copy on loan shows its borrower, date and due date, and its own **Returned** button.

## Bookshop

Bookshop is a phone-sized page for checking your library in a shop. It keeps a copy of your catalog in the browser, so lookups work without a connection. A banner shows when you are offline.

- **Scan ISBN** opens the camera. **Or enter ISBN manually** with **Lookup** works too. A hit shows **In your library** with the title, author, rating, status and an **Open in app** button. A miss shows **Not in your library** with **Add to library** and **Add to wishlist**, which both need a connection.
- **Author lookup** needs at least two letters. Pen names work, so typing a pen name finds the real author. Pick an author to see their books, each with **Open in app**. **Back to authors** returns.
- At the foot, a line shows how many books are cached and when they were synced. **Refresh** pulls a fresh catalog. A warning appears when the catalog is more than 7 days old. If your sign-in has expired, a **Sign in again** link appears.

## AI Assistant

AI Assistant uses an AI provider to look at your library. At the top right, the provider picker lists the providers that are set up, with a count of calls made. Switching providers applies to the whole session, including Bulk add. Under the title, a usage line reads like "This month: 12 calls, 84,210 in / 6,120 out tokens, about $0.35". The cost covers only models that have a price configured. A note says how many do not.

- **Book advisor.** Type a book or author and press **Advise**. You get a verdict, a score out of 10, an analysis, pros and cons, and titles from your library that are similar. **Clear** empties it.
- **Genre cleanup.** **Load books** lists books that need better genres. **Suggest** asks for genres for one book. Suggested genres that the book lacks are marked "(new)". **Accept N new** adds only those. **Accept all** applies the whole suggestion. **Dismiss** discards it.
- **Collection helper.** **Suggest groupings** proposes series or collections for books that have none. **Create** makes one from a suggestion. **Dismiss** clears the list and **Refresh** asks again.
- **Shopping suggestions.** **What should I look for?** proposes books to buy based on your library. **+ List** adds one to your wishlist. **Dismiss** and **Refresh** work as above.

## Duplicates

Duplicates finds pairs that may be the same thing. Four tabs, **Authors**, **Works**, **Books** and **Editions**, each show a count. Authors match on a normalised full name, or on the same surname and first initial. The other three need an exact match after normalising. Each pair shows why it matched.

- **Merge** opens a page where you pick the winner with **Make ... the winner**. A summary says what moves. The loser is deleted and a merge cannot be undone. Empty fields on the winner, such as notes or cover, are filled from the loser. Press **Merge** to confirm, or **Cancel**. Some pairs cannot be merged directly, and the page says why.
- **Dismiss** hides a pair that is not a real duplicate.
- **Show dismissed ...** at the foot of each tab lists dismissed pairs with the date. **Un-ignore** brings one back.

What a merge moves: an author merge moves works and aliases. A work merge moves the books it appears in. An edition merge moves copies. A book merge moves editions, works and tags.

## Privacy

Privacy is a placeholder page. It holds no settings.

## Help

Help lists every page in the top bar with a one-line description, and the key concepts in short. Each entry has a link to the matching section of this manual. A Help link also sits in the footer.

## Book page

Click a cover in the Library to open its Book page. Most changes here save at once.

### Header

The header shows the cover, title, subtitle and author. For a compendium it reads "(+ N more)" after the lead author.

- **Status** is a row of chips: Unread, Reading, Read, Reference. Click one to set it. The Fiction or Non-Fiction chip is read-only here.
- **Rating** is the star row.
- **Lend** or **Returned**, as described under On loan.
- **Edit book details** edits Title, Category, Cover image URL, and **Series / Collection** with **Order / position**.
- **Edit work** (single-Work books) edits that Work: Title, Subtitle, Author(s), contributors, First published and Genres.
- **Add work** and **Add multiple works** attach more Works.
- **Delete book** asks first. It removes the book, its editions, copies and exclusive works. It refuses while a copy is on loan.

### Works

A single-Work book shows a **Details** card with first-published date, series (with position), genres, and **Notes**. A multi-Work book shows **Works in this book** instead.

- **Notes** is a text box that saves by itself shortly after you stop typing, or when you leave the box. The label beside it shows saving, unsaved or saved.
- Each Work expands to show its subtitle, date and genres, with **Edit work** and **Remove from book**. Remove is offered only when the book has more than one Work. A Work that is in no other book is deleted when removed.
- **Reorder** (two or more Works) opens a dialog. Drag a Work, type its position, or use the arrows. Nothing is saved until you press **Save**.
- A box to filter by title or author appears once there are more than ten Works.

### Add work

**Add work** has one **Title** box. As you type, Works already in your library appear. Pick one to attach it and close. Otherwise fill in Author(s), contributors, Subtitle, First published and Genre(s), and press **Save (create new)**. A fresh Work needs a title and at least one author or contributor. **Add multiple works** is the collection grid from Add book, with the same switches and **Save N work(s)**.

### Editions and copies

**Editions & copies** shows totals. Each edition is a panel showing its format, edition number, ISBN and copy count. Open it for publisher and print date, a cover thumbnail, and these buttons:

- **Edit edition** and **Add copy**.
- **Upload photo** (or **Replace photo**) uploads your own cover image from a file or the camera, cropped with the same draggable box as on Add book. A **your photo** chip marks an uploaded cover. The edition form (**Edit edition** and **Add edition**) has the same photo picker; its photo replaces the cover URL when you save.
- Per copy: **Lend** or **Returned**, **Edit** and **Delete**. Delete asks "Delete this copy?" first. Edit changes Condition, Date acquired and Notes.

**Add edition** at the foot adds another printing. It has a **Lookup** button that fills the form from an ISBN, and a first-copy condition.

### Tags

Type in the box to add a tag. Press Enter or pick a suggestion. A tag you have not used before is created. Click the X on a chip to remove it.

**Back to library** returns to the list.

## Bookshelf (the Android app)

Bookshelf holds a copy of your library on the phone, so it answers without a signal. It has three tabs along the bottom: **Find**, **Wishlist** and **Gaps**. It does not edit your library. Changes happen in Bookcase.

### Sync and sign-in

The chip at the top right of Find shows how long ago you synced, or "offline". Tap it to open **Sync & account**. The sheet shows whether you are online, how many books and authors are cached, and when you last synced. **Refresh now** pulls changes from Bookcase and needs sign-in and a connection. **Sign in** and **Sign out** manage your account. Signing out keeps the cached data. **Reset local cache** asks for confirmation, then deletes the cached catalogue, wishlist and covers from the phone, including the Bought marks you made on the phone, and keeps you signed in; the next **Refresh now** downloads the full catalogue again. You sign in only to refresh. The tabs work without signing in. **Close** returns. The Wishlist and Gaps screens also have a **Sync** button in their toolbar that opens the same sheet.

### Find

Type a title, author or ISBN in the search box. After two letters, results appear in groups: **Authors** and **Works**. The **All**, **Authors** and **Works** buttons narrow them. Tap an author to see their books. Tap a book to open its page in Bookcase in your browser.

On an author's screen you see their books from your library. When the phone is online, the list also adds the author's other works from Bookcase's online lookup. Works you do not own are marked "Not owned", and **Missing only** shows just those. "You own every work listed for this author" appears when none are missing.

To check an ISBN, type or paste it and press Enter (the search box says "Press Enter to look up this ISBN"), or press **Scan**. Scan asks for camera permission, shows the camera in a strip, reads the first EAN barcode it sees and opens the result. Press **Cancel** to close the camera.

The result screen answers from the phone's cache first:

- A match shows the cover, title, authors, status and rating, the ISBN and **Open in app**. A compendium also lists its works, and a book with several editions lists the ones you own.
- An "On your wishlist" flag shows when the ISBN is on your wishlist.
- A miss shows "Not in your library." with **Add in app**, which opens Bookcase's Add page in your browser.

When the phone is online and the cache misses, it shows "Checking online..." and asks Bookcase's online lookup about the ISBN. If it gets an answer, the answer replaces the miss card. It then reads one of three ways:

- **In your library**: you own this exact edition and the phone's copy is out of date. A note says to sync, and a **Sync** button opens the sheet.
- **You have this work, not this edition**: the card lists the editions you own.
- **Not in your library**: it shows the title, author, publisher and year, with **Add in app**.

When an answer is found, an **Other editions of this work** row lists more editions with a "You own this" mark. It stays greyed, labelled "online only", until the list loads, and stays that way if the list cannot load. Owned answers also have **Open in app**.

### Wishlist

The Wishlist tab reads the wishlist saved on the phone. **Refresh wishlist** pulls the latest from Bookcase. **Priority**, **Author** and **Series** change the order. Priority groups the list under High, Medium and Low headings. Series lists series books first, in order. Each row shows the cover, title, author, a priority badge and a series position. **Bought** hides the row on the phone only. It does not change the wishlist in Bookcase.

### Gaps

The Gaps tab lists numbered series where you own some books but not all. Each card shows "N of M owned", a progress bar and a **Missing** list of positions. It reloads each time you open the tab.
