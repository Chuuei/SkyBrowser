# Optional example photographs

No scientific images are distributed in this repository. Use your own supported
FITS/XISF files, or download an image from
[The Wizard's Library](https://astrowizard.lukomatico.com/library.html).
Check the image provider's terms before redistributing or publishing photographs.

Unzip a download and add its folder with **Image library → Add folder**. Automatic placement
requires supported astrometry: XISF Revision 1 or the supported FITS TAN WCS. A file
without it can use **Blind solve & place** after configuring ASTAP, or manual placement. Recognized Bayer metadata produces color previews
automatically; unsupported CFA patterns remain grayscale.

You may store local files here; Git ignores everything except these instructions
and `examples.txt`. Keep distributed `.xish` headers with their sidecars.

The optional overlap tests/manual screenshot helper need a wider plate-solved
Horsehead FITS field and a smaller XISF field covering the same region. These
particular photographs are not guaranteed to be available from the linked provider.
Pass an external sample directory using the screenshot instructions in
[manual maintenance](../docs/manual/README.md). Do not rename unrelated images just
to satisfy a fixture selector; the geometry is part of the check.
