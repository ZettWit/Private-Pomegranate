# Third-party notices

Pomegranate's own code is MIT-licensed (see LICENSE). It bundles the components below, each under its own license.

## FFmpeg 9.0.2 (GPL)

Pomegranate runs FFmpeg (`ffmpeg.exe`, `ffprobe.exe` and their DLLs) as a separate program for audio and video conversions. The bundled build is BtbN's "gpl-shared" build, licensed under the GNU General Public License version 3 or later; the full text ships as `engines/ffmpeg/LICENSE.txt`.

- FFmpeg project and source: https://ffmpeg.org/download.html
- Build scripts and the exact build used: https://github.com/BtbN/FFmpeg-Builds (release `autobuild-2026-11-18-12-39`, `ffmpeg-n9.0.2-57-gd838f9f9ae`)

## dnglab 0.8.0 (LGPL-2.1)

Runs as a separate program to convert camera RAW files to DNG. Source: https://github.com/dnglab/dnglab

## Magick.NET 15.1.0 (Apache-2.0) and ImageMagick 7.1.2 (ImageMagick License)

Used in-process for image conversions. Magick.NET: https://github.com/dlemstra/Magick.NET. ImageMagick license: https://imagemagick.org/script/license.php. The native ImageMagick library includes other open-source libraries (for example libheif, libraw, librsvg, libwebp, libaom, libjpeg-turbo, libpng, libtiff and zlib), each under its own license; the Magick.NET repository lists them.

## Pandoc 3.14 (GPL-2.0-or-later)

Runs as a separate program for document conversions. Source: https://github.com/jgm/pandoc (release 3.14). The bundled copy's notice ships as `engines/pandoc/COPYRIGHT.txt`.

## PdfPig 0.1.16 (Apache-2.0)

Used in-process to read text and pictures from PDFs. https://github.com/UglyToad/PdfPig

## ExcelDataReader 3.9.0 (MIT)

Used in-process to read XLS and XLSX files. https://github.com/ExcelDataReader/ExcelDataReader

## MiniExcel 1.47.1 (Apache-2.0)

Used in-process to write XLSX files. https://github.com/mini-software/MiniExcel

## PDFsharp 6.2.4 (MIT)

Used in-process for the PDF tools (merge, split, pages, compress, metadata). https://github.com/empira/PDFsharp. Pinned to 6.2.4, the last MIT-licensed release; later releases are AGPL-3.0.

## ZXing.Net 0.16.11 (Apache-2.0)

Used in-process to read QR codes and barcodes. https://github.com/micjahn/ZXing.Net

## Not bundled

Pomegranate also uses, when they are installed on the PC, Microsoft Edge (printing simple documents to PDF), Microsoft Office or LibreOffice (Office layouts, OpenDocument and old formats), Windows' own PDF renderer and Windows' text recognition (Copy text). They are the PC's own software under their own licenses; Pomegranate doesn't ship them.

## SharpCompress 0.50.10 (MIT)

Used in-process to read archives. https://github.com/adamhathcock/sharpcompress
