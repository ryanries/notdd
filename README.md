notdd
=====

This command-line program performs low-level writes to physical drives. You supply the raw "image" file. If the image is not a multiple of the drive's sector size (usually 512 bytes,) then it will be padded with zeroes until it is.

This is useful for writing custom boot loaders to a disk. It might also be useful as a "secure erase" utility.

Current limitations: 

- I have not done extensive testing with 512e or 4K-native "Advanced Format" drives. It might work fine, it might not. I only tested with standard legacy drives with 512-byte sectors.

- The methods used for reading the source image file and padding it with zeroes are not sophisticated or efficient, so this means the program will probably not work well with very large images. Works fine for small images though, such as boot loaders.

- I need to figure out how to get this to write a boot sector to a VHD so that it will boot with Hyper-V. A virtualization platform would make testing so much easier. Does work with Bochs though.

```
notdd v1.0 - Writes bytes to raw disk sectors.
Written by Ryan Ries 2014 - myotherpcisacloud.com

Usage: C:\>notdd image=<image>
              device=<physical_device_number>
              startingsector=<starting_sector>
              [force]
              [list]

Example:
  C:\>notdd image=C:\Data\image.bin device=0 startingsector=0
  Writes the image.bin file to \\.\PHYSICALDRIVE0 starting at sector 0.

Example:
  C:\>notdd image=""C:\Long Path\image.bin"" device=0 startingsector=0
  If the image file path has spaces in it, use quotation marks.

Example:
  C:\>notdd image=C:\Data\image.bin device=1 startingsector=0 force
  Use the force parameter to not be asked for confirmation.

Example:
  C:\>notdd list
  List available, writable physical devices.

WARNING: This program can ruin the existing formatting and 
filesystem of the drive!
```

