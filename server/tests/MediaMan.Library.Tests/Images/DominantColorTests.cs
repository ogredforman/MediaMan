using MediaMan.Fakes;
using MediaMan.Library.Images;

namespace MediaMan.Library.Tests.Images;

public sealed class DominantColorTests
{
    [Fact]
    public void A_solid_image_is_its_own_color()
    {
        DominantColor.FromImage(TestImages.Solid("#336699")).Should().Be("#336699");
    }

    [Fact]
    public void The_most_common_color_wins()
    {
        var image = TestImages.Bmp(10, 10, (x, _) => x < 7 ? ((byte)200, (byte)40, (byte)40) : ((byte)40, (byte)40, (byte)200));

        DominantColor.FromImage(image).Should().Be("#C82828");
    }

    [Fact]
    public void Letterbox_bars_are_ignored_when_the_picture_has_color()
    {
        // Two thirds black bars, one third picture: the picture still sets the tint.
        var image = TestImages.Bmp(9, 9, (_, y) => y is >= 3 and < 6 ? ((byte)30, (byte)120, (byte)60) : ((byte)0, (byte)0, (byte)0));

        DominantColor.FromImage(image).Should().Be("#1E783C");
    }

    [Fact]
    public void An_image_that_is_nearly_all_black_is_black()
    {
        var image = TestImages.Bmp(100, 1, (x, _) => x == 0 ? ((byte)200, (byte)0, (byte)0) : ((byte)5, (byte)5, (byte)5));

        DominantColor.FromImage(image).Should().Be("#050505");
    }

    [Fact]
    public void Transparent_pixels_never_count()
    {
        byte[] rgba = [255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 0, 0, 255, 255];

        DominantColor.FromPixels(rgba).Should().Be("#0000FF");
        DominantColor.FromPixels([255, 0, 0, 0]).Should().BeNull();
    }

    [Fact]
    public void Data_that_cant_be_decoded_has_no_color()
    {
        DominantColor.FromImage("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray()).Should().BeNull();
        DominantColor.FromImage([0xFF, 0xD8, 0xFF, 0xE0, 0x00]).Should().BeNull();
        DominantColor.FromImage([]).Should().BeNull();
    }
}

public sealed class ImageFormatsTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, ".jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, ".png")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, ".gif")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, ".webp")]
    public void Formats_are_recognised_by_their_leading_bytes(byte[] data, string extension)
    {
        ImageFormats.Sniff(data)!.Extension.Should().Be(extension);
    }

    [Fact]
    public void Bmp_and_svg_are_recognised()
    {
        ImageFormats.Sniff(TestImages.Solid("#000000")).Should().Be(ImageFormats.Bmp);
        ImageFormats.Sniff("﻿<?xml version=\"1.0\"?>\n<svg></svg>"u8).Should().Be(ImageFormats.Svg);
    }

    [Fact]
    public void Other_data_is_not_an_image()
    {
        ImageFormats.Sniff("<html><body>404</body></html>"u8).Should().BeNull();
        ImageFormats.Sniff("<?xml version=\"1.0\"?><rss/>"u8).Should().BeNull();
        ImageFormats.Sniff([]).Should().BeNull();
    }

    [Fact]
    public void Cached_file_names_map_back_to_their_content_type()
    {
        ImageFormats.ContentTypeOf("0192.jpg").Should().Be("image/jpeg");
        ImageFormats.ContentTypeOf("0192.SVG").Should().Be("image/svg+xml");
        ImageFormats.ContentTypeOf("0192.exe").Should().BeNull();
    }
}
