# Library scrolling

The Recently added shelf uses an ItemsRepeater inside the library's single ScrollViewer.
Cards have a fixed 208 × 266 layout so creating and recycling off-screen cards cannot
change the row size as artwork loads. Virtualization and the artwork cache remain enabled.

Loading another page preserves the unchanged prefix of LibraryTracks. It appends new
items instead of clearing the collection and rebuilding all visible cards. Choosing
an already selected filter also leaves the existing containers intact. Changing the
filter replaces only the changed tail. A late pinned-playlist response no longer forces
the library back to the top; the initial track-page replacement still resets the page.

## Windows verification

Use a signed-in account with several pages of saved songs. Scroll rapidly down and up
using the mouse wheel, both over artwork and over card buttons. Load another page while
partway down and confirm the current cards stay visible. Re-select All, toggle Liked,
then return to All. Repeat at compact width and 100%/150% display scaling. Check that
row spacing stays consistent, cards do not clip, and memory remains bounded after
repeated passes. Check search, playlist details and Queue separately; they use ListView.

These changes address identified rebuild/layout costs. Mouse-wheel frame pacing still
needs a live Windows run; compilation or UI Automation scrolling alone cannot prove it.
