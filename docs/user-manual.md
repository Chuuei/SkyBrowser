# SkyBrowser

## Beginner's user manual

Explore your astronomical photographs in their place on the sky. SkyBrowser combines your FITS and XISF images with a simple star map. You can look around, compare photographs and return to a saved view later.

<!-- figure height=250 crop=0,0,1424,881 markers=1:0.676:0.044;2:0.218:0.25;3:0.218:0.67;4:0.951:0.2 -->
![The main window, showing a wide Horsehead field and a smaller image of the same region.](manual/screenshots/main-window.png)

- **1. Top bar:** open Options, open or save a session, and add images.
- **2. Image collection:** browse folders, search your loaded images and choose one.
- **3. Selected image:** change the chosen image's appearance, visibility and placement. Drag the divider above this panel to resize it.
- **4. Sky view:** drag to look around and scroll to zoom. The bottom status bar reports progress and problems.

| Start here | Page | Explore further | Page |
| --- | --- | --- | --- |
| Load your first images | 2 | Overlapping photographs | 6 |
| Navigate and find a target | 3 | Location and observing time | 7 |
| Collection and image details | 4 | Options and the cache | 8 |
| Choose an image display | 5 | Loading and cancellation | 9 |
| Save and reopen a session | 10 | Place an unsolved image | 11 |
| Troubleshooting | 12 | Quick reference | 13 |

> **Your originals stay safe.** Display changes, blending and placement do not rewrite your scientific image files. Keep those originals even after saving a session.

---

# 01 / Your first images

## Start with the defaults

1. Open **SkyBrowser.exe** from the published application folder.
2. On the welcome screen, keep **Automatic · dark sky (fast)**. It makes dark, unstretched data easier to see without the slower cleanup steps.
3. Your observing location is optional. Leave it empty to explore the sky as an atlas, or choose **Choose on map...** to set it now. You can change it later in Options.
4. Choose **Save & start**. **Use defaults** also saves the factory choices. **Not now** continues without saving setup; it can appear again next time.

<!-- figure height=250 crop=0,0,644,562 -->
![The welcome screen appears when no saved settings or recovery backup exists.](manual/screenshots/first-run-setup.png)

## Load and frame a photograph

1. Choose **Browse folder...** on the left and select a folder containing images. This replaces the current collection with that folder's images.
2. Wait for loading to finish. Click an entry, then **Go to image** to bring it into view. Double-clicking a collection entry also frames it.
3. Drag the sky to move around and scroll to zoom in or out.

Use **+ Add images** to append files, or **Add...** beside Browse folder to append another folder. You can also drop files or folders onto the window. **Recent folders...** returns to folders visited before, even after restarting.

> **Supported files:** .fits, .fit, .fts, .xisf and .xish. Keep a distributed XISF header and its associated data files together. Folder loading reads that folder's files, without searching its subfolders. An image needs stored sky-position information to appear automatically; see page 11 for **Needs placement**.

---

# 02 / Explore the sky

## Move around

| Action | How to do it |
| --- | --- |
| Look in another direction | Drag an empty part of the sky with the left mouse button. |
| Zoom | Scroll the mouse wheel, or use the + and - buttons above the sky. |
| Frame the selected image | Choose Go to image, or press F while the sky has focus. |
| Return to a wide view | Choose Overview, or press Home while the sky has focus. |

The switches control the grid, constellation lines, labels and image outlines. Atlas mode uses **RA/Dec grid**; an observing location switches to **Alt/Az grid**, fixed to the local horizon.

## Find a named object

1. Expand **Find in the sky** in the upper left of the sky view.
2. Type a name or catalogue number, such as **M31**, **IC 434**, **NGC 253** or **Horsehead**.
3. Optionally choose Messier, Caldwell, NGC or IC to narrow the catalogue.
4. Open the results list and choose an object. The sky turns toward it, adds a marker and fills in its coordinates.

<!-- figure height=200 crop=350,140,678,359 -->
![The expanded finder. NGC 253 and Caldwell 65 are names for the same galaxy.](manual/screenshots/target-picker-selected.png)

The finder includes Messier and Caldwell objects plus NGC and IC entries with known positions. Initially, only a small selection of prominent targets is labelled. **Clear added** removes the markers you have added; it keeps your photographs.

The shared **Go** button returns to the selected target. You can also enter a known **RA** and **Dec**, then choose Go. Both input boxes use decimal degrees. The sky's RA readout may use hours, so do not paste an hours/minutes/seconds value into the degree input. Collapse the finder again to recover space.

> **A catalogue marker is a signpost.** Finding an object does not download its photograph. Add your own image files to see photographs at that position.

---

# 03 / Your image collection

## Choose, hide and search

Click an image's entry to show its controls under **Selected image**. Its checkbox hides or shows it on the sky. **Remove** takes it out of this collection; it does not delete the source file. The arrow buttons reorder entries. Drag the horizontal divider to give the collection or the selected-image controls more room.

Use **Search names & metadata** to find an image by filename, target, telescope, filter or other retained metadata. **ic434**, **IC 434** and **IC-434** match the same target. Multiple words narrow the results: try **IC 434 H-alpha**. Press **Ctrl+F** to focus search; the x button or Escape in the search box clears it.

> **Search filters the list.** An image hidden from the search results can still be visible on the sky. Use its checkbox to hide it from the sky itself.

## Inspect the information behind a photograph

Choose the small circled **i** beside an entry to open **Image details**. The window shows its preview, source path, original and preview dimensions, file size, display mode and sky placement. Search the metadata table to find a keyword, value or comment. Select a row to read and copy its value in the box below.

<!-- figure height=335 crop=0,0,884,762 -->
![Image details for the narrower Horsehead image. Metadata and the original source path are available together.](manual/screenshots/image-details.png)

Large metadata values may be shortened for display; the original file keeps the full information. The sky uses resized display previews. **Double-click a photograph in the sky** to open its original file for full-resolution work in the Windows default application. With overlapping photographs, the foremost image under the pointer opens.

---

# 04 / Make images easy to see

Many astronomical files store **linear**, or unstretched, data. They can look almost black until a display stretch makes faint detail visible. SkyBrowser changes the preview, while keeping your source file intact.

Select an image and use its **Display** list. Changing a mode may take a moment to process.

| Display choice | When to use it |
| --- | --- |
| Automatic · dark sky (fast) | Start here. Uses a stored display function when present; otherwise detects dark linear data and stretches it. It skips cosmetic cleanup. |
| Linear / already stretched | Use when your image already looks right and should keep its existing exposure. |
| Library auto stretch (25%) | An alternative, usually brighter automatic display stretch. |
| Stored display function | Uses the display settings saved inside the image file. Without them, it uses a linear display. |
| Dark sky + cleanup | Adds the enabled background, noise and detail adjustments. The first processing pass takes longer. |

## Adjust one image

Expand **Sky background & cleanup** in Selected image. A lower sky-background target makes the background darker. Start near **10%**, then adjust gently. Choose the cleanup options you want and select **Apply finish**. This switches that image to **Dark sky + cleanup**. If a large nebula seems affected, try turning off **Reduce broad sky gradients**.

## Choose defaults for future loads

Open **Options > Images**, choose a default stretch and background target, then **Save options**. Automatic uses only the background target, even when the cleanup boxes are checked.

<!-- figure height=210 crop=24,138,720,508 -->
![The Images tab with the default Automatic display and a 10% background target. Cleanup boxes apply to Dark sky + cleanup.](manual/screenshots/options-images.png)

Saved per-image choices and sessions take precedence over global defaults. Saving Options does not reprocess the images already on screen. **Reset defaults** in Selected image applies your saved image defaults to that image.

---

# 05 / Compare overlapping images

SkyBrowser can show a wide photograph and a closer view of the same region together. Matching uses the images' positions and sky coverage, so their filenames do not need to match.

1. Add both photographs. For example, load the wider Horsehead FITS image and the narrower IC434 XISF image from the project's examples folder, if available.
2. Turn on **Blend overlaps** above the sky.
3. Frame either image with **Go to image**. The photograph covering more sky forms the background. A smaller overlapping image becomes a translucent inset.
4. Move the pointer over the inset to reveal it at full opacity. Move away to restore the blend.

<!-- pair -->
<!-- figure height=280 crop=680,193,1075,774 -->
![Pointer away: the smaller field is blended into the wider background.](manual/screenshots/horsehead-blended.png)
<!-- figure height=280 crop=680,193,1075,774 -->
![Pointer over the inset: its own image is shown at full opacity.](manual/screenshots/horsehead-hover.png)

## Opacity and ordering

The selected image's **Opacity** slider controls how strongly it appears. With blending enabled, an inset normally uses 60% of that chosen opacity. Hovering temporarily makes it fully opaque; it does not change the saved slider value.

Turn **Blend overlaps** off to use collection order instead. Later entries are drawn above earlier ones; use the arrow buttons to change that order. Hiding a larger background can make its remaining inset opaque when no larger overlapping background remains.

> **Larger means more sky, not more pixels.** A high-resolution close-up can still be the smaller inset. Blending and hovering change transparency only; they do not stretch the image again.

---

# 06 / Set your location and time

This is optional. Without a location, SkyBrowser works as a celestial atlas. With a location, it rotates the view to your local horizon and starts facing south.

1. Open **Options > Observer** and choose **Choose on map...**.
2. Click your location on the map. Drag to pan and scroll to zoom. For precision, enter latitude and longitude; negative numbers mean south or west.
3. Leave **Use current system time** checked for a live sky, or uncheck it to choose a fixed date and time.
4. Choose **Apply & face south** to return to Options, then **Save options** to apply and remember the choice.

<!-- figure height=225 crop=0,0,984,672 -->
![Demonstration only: latitude 0, longitude 0 and a fixed example time in UTC.](manual/screenshots/observer-location.png)

## Move through time

With a location selected, **Scene time** appears below the sky. Drag its slider backward or forward up to 12 hours. This pauses the live clock; **Live now** returns to the current system time. **-1 day** / **+1 day** move 24 hours and recenter the slider. With the slider focused, arrow keys move one minute; Page Up/Down move one hour.

<!-- figure height=63 crop=330,766,1034,846 -->
![The scene timeline, using a neutral example clock shown in UTC.](manual/screenshots/scene-timeline.png)

The readout uses your computer's time zone and UTC offset, not a zone inferred from the map. Options also accepts fixed UTC times. Moving time keeps your direction and zoom and does not reprocess images. Sessions save the scene time; your startup options stay unchanged.

The view starts south, 20 degrees up; **Overview** returns there. **ZENITH** marks straight up (90 degrees). **Polaris** is roughly your latitude above north, reaching overhead only near the North Pole. Use **Clear location > Save options** to return to the atlas and hide the timeline.

> **A simple sky guide:** the map works offline. The horizon does not include mountains, atmospheric effects, daylight or weather. Supported observing dates run from 1900 to 2100.

---

# 07 / Options and a faster library

Choose **Options** in the top bar. Use **Save options** to apply changes; **Cancel** discards staged changes. The tabs cover:

| Tab | What it controls |
| --- | --- |
| Images | Default display stretch, sky background and cleanup choices. |
| Observer | Your optional startup location and live or fixed observing time. |
| Cache | Where processed previews are stored and how much space they can use. |
| Sky | Startup grid, constellations, labels, outlines and overlap blending. |

## Let the cache do the repeated work

The first load may be slow, especially for large images or cleanup. SkyBrowser saves processed previews locally and reuses them on later visits, including after closing and restarting the program. New or changed source files and new display settings can require processing again.

<!-- figure height=245 crop=24,99,720,508 -->
![The Cache tab shows the 10 GB default limit and the space already used. Your folder path and usage will differ.](manual/screenshots/options-cache.png)

The default limit is **10 GB**. This is a maximum for stored preview data, not space reserved immediately. Older previews are removed as the limit is reached; database overhead can use some extra disk space. Choose a fast local drive with enough free space for a responsive experience.

Changing the cache folder copies cached content to the new location when you save. Old cache files remain in the previous folder. **Clear saved previews** clears the active cache immediately, even if you later cancel Options. It keeps loaded images, remembered image choices and recent folders; later loads may need processing again.

> **Where settings live:** Options are saved for your Windows user in **%LOCALAPPDATA%\SkyBrowser\settings.json**, separately from the preview database. The Cache tab also shows the options-file path. A cache is disposable; it is not a backup of your images or sessions.

---

# 08 / Loading and cancellation

Longer loads open a progress window after a short delay. Fast cached visits may finish before it appears.

<!-- figure height=250 crop=0,0,524,366 -->
![A loading example: the overall bar counts files; the lower bar shows progress within the current processing step.](manual/screenshots/loading-progress.png)

The window shows the current filename, completed and remaining entries, the processing step and counts of ready, cached, failed or already-loaded images. Folder imports count files; one file can contain multiple images. Restoring a session counts its saved image entries. A pulsing bar means the current step has no percentage to report.

Choose **Cancel loading**, press Escape in that window, or close it to stop. Some decoding and cleanup steps must finish before cancellation completes. The display shows **Stopping...** while it waits.

- During an import, images already added stay available; remaining files are not started.
- During a display change, cancellation keeps the previous preview.
- During a session restore, cancellation keeps the sky you had before opening it.

If a file fails, other files can still load. Read the message in the bottom status bar and choose **Details** there for more information. The status-bar Details button is an operation log; the small circled i in an image entry opens that image's metadata.

> **If loading seems slow:** let the first visit finish so its previews can be saved. Keep Automatic as your default for quicker processing, and choose cleanup only for images that benefit from it.

---

# 09 / Save a view and return later

A **session** remembers a collection and its view. Use one for a project, a target comparison or a particular observing setup.

<!-- workflow -->

## Save your current work

1. Load the images you want, set their display modes and arrange the sky.
2. Choose **Save session** in the top bar.
3. Choose a folder and a descriptive name, such as **Horsehead comparison.sky.json**.

The session keeps source-image references, layer order, visibility, opacity, display and cleanup choices, manual placements, added catalogue markers, observer/time settings and your sky direction and zoom.

## Reopen it

1. Choose **Open session** and select the .sky.json file.
2. Let its images load. Saved previews are reused when available.
3. Continue exploring. Save the session again when you want to keep a later arrangement.

> **A session points to your originals.** It does not contain copies of the FITS or XISF files. Keep the source images available. Paths are stored relative to the session where possible, so moving a whole project folder together is more reliable than moving only the session file.

| Use this | To remember this |
| --- | --- |
| Options | Your defaults for later image loads and application starts. |
| Session | A particular set of images, their choices and the current sky view. |
| Preview cache | Processed displays that make later visits quicker. |

Missing session images are reported in **Details**; available ones can still load. A session's observer and display choices can differ from your startup defaults. Opening a session does not replace those saved startup defaults.

## A simple routine

Browse a folder, frame an image, adjust its appearance if needed, then save a session. Next time, reopen that session to restore the arrangement, or use Recent folders when you just want to browse the folder again.

---

# 10 / When an image needs placement

Automatic placement requires a stored **astrometric solution**, often called a **plate solve**. It tells the program which sky direction belongs to each part of an image. A target name or acquisition coordinates alone are not enough to determine its width, rotation and possible reflection.

An image marked **Needs placement** is loaded into the collection but has no usable position on the sky. SkyBrowser does not include a plate solver. For accurate alignment, solve the image in suitable astronomy software and save the solution in the file, then load the updated image.

## Optional: place it by hand

If you already know the necessary values, select the image and expand **Manual sky placement**. Enter:

| Input | Meaning |
| --- | --- |
| Center RA | Right ascension of the image center, in decimal degrees from 0 to 360. |
| Center declination | Degrees north or south of the celestial equator, from -90 to +90. |
| Width | The full angular width of the image in degrees, not its pixel count. |
| Rotation | Zero means north up and east left. Positive values turn the image counterclockwise on the sky. |
| Mirror horizontally | Reverses the image horizontally when needed to match its orientation. |

Choose **Apply placement**, then **Go to image**. Inspect the alignment against known objects and nearby images. Choose **Use file solution** to remove the manual override and try the image's stored solution again. Manual placement is kept in a saved session.

> **Manual placement is approximate.** A plausible position is not proof of an accurate alignment. Wide or distorted fields especially benefit from a proper plate solve. Moving or stretching a preview does not repair the original file's astrometric metadata.

## If a solved image still cannot be placed

Read its warning in Selected image or the status-bar **Details** log. Some stored projections, distortion models and coordinate reference systems are not supported. A compatible J2000/ICRS solution or a manual placement may be needed. Use the image's circled i button to inspect its retained metadata.

---

# 11 / Common questions

| What you notice | What to try |
| --- | --- |
| An image is listed but not visible | Check its visibility checkbox and opacity. Clear the collection search, select it and choose Go to image. If it says Needs placement, follow page 11. |
| A photograph is too bright | Try Automatic or Linear / already stretched if it was previously processed. For cleanup, lower the sky-background target and select Apply finish. |
| A photograph is almost black | It may be linear data. Try Automatic or Dark sky + cleanup, and allow processing to finish. |
| A nebula changes too much with cleanup | Try disabling Reduce broad sky gradients or other cleanup options. Automatic provides a stretch without those cosmetic steps. |
| Fewer entries appear in the collection | Look for an active search. Clear it with the x button. Filtering the list does not hide photographs from the sky. |
| A new default does not change a loaded image | Options do not reprocess the current collection. Use Reset defaults in Selected image. Remembered per-image choices and sessions can override global defaults. |
| A file fails to open | Read status-bar Details. Check that it still exists, its format is supported, and any distributed XISF data files are present. |
| Double-clicking the sky cannot open a source | Install or select a Windows application associated with FITS/XISF, and check that the original file is still available. |
| The local sky time seems wrong | Check your computer clock and time zone. For a fixed time, confirm Computer local time versus UTC. The map marker does not choose a time zone. |
| Loading is slow again after an update | Updated processing, changed sources, a new stretch variant or an evicted preview may need another processing pass. Later cached visits should be quicker. |

## Know which Details to open

The **circled i beside an image** shows that image's dimensions, source-file facts and metadata. The **Details button in the bottom status bar** shows loading messages and errors. Start with the status-bar log when something fails.

> **Settings will not save?** Check that your Windows user can write to the location shown in Options. For a cache-folder error, choose an accessible local folder. A failed save keeps the options or setup window open with an explanation.

---

# 12 / Keep this page handy

## Everyday controls

| Control | Result |
| --- | --- |
| Left-drag / mouse wheel over sky | Look around / zoom. |
| Arrow keys / + / - | Navigate / zoom while the sky has focus. |
| F / Home | Frame the selection / return to the overview while the sky has focus. |
| Ctrl+F | Focus collection search. |
| Escape in collection search | Clear the search. |
| Escape in the loading window | Request cancellation. |
| Double-click a collection entry | Frame that image. |
| Double-click a photograph in the sky | Open its original in the Windows default application. |
| Drag the collection divider | Resize the collection and Selected image panels. |

## A few useful words

- **FITS / XISF:** scientific image-file formats that can store pixels and information about the observation.
- **Metadata:** information saved with an image, such as target, filter, telescope, exposure and coordinates.
- **Stretch:** a display adjustment that makes faint detail visible. It does not change your source image here.
- **RA / Dec:** right ascension and declination, the sky's position coordinates. SkyBrowser's input boxes use decimal degrees.
- **Field of view:** how much sky you can see. A smaller angle gives a closer view.
- **Plate solve / astrometric solution:** information connecting image positions to sky coordinates.
- **Cache:** saved processed previews used to speed up future visits, including after restarting.
- **Session:** a saved .sky.json arrangement that refers to your original files.

## A good first session

Keep **Automatic** as your default. Browse one folder, select an image and choose **Go to image**. Look around, try the finder, then save a session. Add a location or cleanup only when you need it.

This guide describes the current Windows desktop application. Screenshots use anonymous demonstration paths, metadata and observing settings. Filenames, values and layout can differ on your computer. Sky-catalogue and map attributions are supplied with the application in **THIRD-PARTY-NOTICES.md**.
