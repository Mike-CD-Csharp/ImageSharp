// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Threading;
using SixLabors.ImageSharp.IO;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Formats.Pbm
{
    /// <summary>
    /// Performs the PBM decoding operation.
    /// </summary>
    internal sealed class PbmDecoderCore : IImageDecoderInternals<PbmDecoderOptions>
    {
        private int maxPixelValue;

        /// <summary>
        /// The general configuration.
        /// </summary>
        private readonly Configuration configuration;

        /// <summary>
        /// The colortype to use
        /// </summary>
        private PbmColorType colorType;

        /// <summary>
        /// The size of the pixel array
        /// </summary>
        private Size pixelSize;

        /// <summary>
        /// The component data type
        /// </summary>
        private PbmComponentType componentType;

        /// <summary>
        /// The Encoding of pixels
        /// </summary>
        private PbmEncoding encoding;

        /// <summary>
        /// The <see cref="ImageMetadata"/> decoded by this decoder instance.
        /// </summary>
        private ImageMetadata metadata;

        /// <summary>
        /// Initializes a new instance of the <see cref="PbmDecoderCore" /> class.
        /// </summary>
        /// <param name="options">The decoder options.</param>
        public PbmDecoderCore(PbmDecoderOptions options)
        {
            this.Options = options;
            this.configuration = options.GeneralOptions.Configuration;
        }

        /// <inheritdoc/>
        public PbmDecoderOptions Options { get; }

        /// <inheritdoc/>
        public Size Dimensions => this.pixelSize;

        /// <inheritdoc/>
        public Image<TPixel> Decode<TPixel>(BufferedReadStream stream, CancellationToken cancellationToken)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            this.ProcessHeader(stream);

            var image = new Image<TPixel>(this.configuration, this.pixelSize.Width, this.pixelSize.Height, this.metadata);

            Buffer2D<TPixel> pixels = image.GetRootFramePixelBuffer();

            this.ProcessPixels(stream, pixels);
            if (this.NeedsUpscaling())
            {
                this.ProcessUpscaling(image);
            }

            return image;
        }

        /// <inheritdoc/>
        public IImageInfo Identify(BufferedReadStream stream, CancellationToken cancellationToken)
        {
            this.ProcessHeader(stream);

            // BlackAndWhite pixels are encoded into a byte.
            int bitsPerPixel = this.componentType == PbmComponentType.Short ? 16 : 8;
            return new ImageInfo(new PixelTypeInfo(bitsPerPixel), this.pixelSize.Width, this.pixelSize.Height, this.metadata);
        }

        /// <summary>
        /// Processes the ppm header.
        /// </summary>
        /// <param name="stream">The input stream.</param>
        private void ProcessHeader(BufferedReadStream stream)
        {
            Span<byte> buffer = stackalloc byte[2];

            int bytesRead = stream.Read(buffer);
            if (bytesRead != 2 || buffer[0] != 'P')
            {
                throw new InvalidImageContentException("Empty or not an PPM image.");
            }

            switch ((char)buffer[1])
            {
                case '1':
                    // Plain PBM format: 1 component per pixel, boolean value ('0' or '1').
                    this.colorType = PbmColorType.BlackAndWhite;
                    this.encoding = PbmEncoding.Plain;
                    break;
                case '2':
                    // Plain PGM format: 1 component per pixel, in decimal text.
                    this.colorType = PbmColorType.Grayscale;
                    this.encoding = PbmEncoding.Plain;
                    break;
                case '3':
                    // Plain PPM format: 3 components per pixel, in decimal text.
                    this.colorType = PbmColorType.Rgb;
                    this.encoding = PbmEncoding.Plain;
                    break;
                case '4':
                    // Binary PBM format: 1 component per pixel, 8 pixels per byte.
                    this.colorType = PbmColorType.BlackAndWhite;
                    this.encoding = PbmEncoding.Binary;
                    break;
                case '5':
                    // Binary PGM format: 1 components per pixel, in binary integers.
                    this.colorType = PbmColorType.Grayscale;
                    this.encoding = PbmEncoding.Binary;
                    break;
                case '6':
                    // Binary PPM format: 3 components per pixel, in binary integers.
                    this.colorType = PbmColorType.Rgb;
                    this.encoding = PbmEncoding.Binary;
                    break;
                case '7':
                // PAM image: sequence of images.
                // Not implemented yet
                default:
                    throw new InvalidImageContentException("Unknown of not implemented image type encountered.");
            }

            stream.SkipWhitespaceAndComments();
            int width = stream.ReadDecimal();
            stream.SkipWhitespaceAndComments();
            int height = stream.ReadDecimal();
            stream.SkipWhitespaceAndComments();
            if (this.colorType != PbmColorType.BlackAndWhite)
            {
                this.maxPixelValue = stream.ReadDecimal();
                if (this.maxPixelValue > 255)
                {
                    this.componentType = PbmComponentType.Short;
                }
                else
                {
                    this.componentType = PbmComponentType.Byte;
                }

                stream.SkipWhitespaceAndComments();
            }
            else
            {
                this.componentType = PbmComponentType.Bit;
            }

            this.pixelSize = new Size(width, height);
            this.metadata = new ImageMetadata();
            PbmMetadata meta = this.metadata.GetPbmMetadata();
            meta.Encoding = this.encoding;
            meta.ColorType = this.colorType;
            meta.ComponentType = this.componentType;
        }

        private void ProcessPixels<TPixel>(BufferedReadStream stream, Buffer2D<TPixel> pixels)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            if (this.encoding == PbmEncoding.Binary)
            {
                BinaryDecoder.Process(this.configuration, pixels, stream, this.colorType, this.componentType);
            }
            else
            {
                PlainDecoder.Process(this.configuration, pixels, stream, this.colorType, this.componentType);
            }
        }

        private void ProcessUpscaling<TPixel>(Image<TPixel> image)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            int maxAllocationValue = this.componentType == PbmComponentType.Short ? 65535 : 255;
            float factor = maxAllocationValue / this.maxPixelValue;
            image.Mutate(x => x.Brightness(factor));
        }

        private bool NeedsUpscaling() => this.colorType != PbmColorType.BlackAndWhite && this.maxPixelValue is not 255 and not 65535;
    }
}
