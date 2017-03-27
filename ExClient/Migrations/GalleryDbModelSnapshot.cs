﻿using ExClient.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System;

namespace ExClient.Migrations
{
    [DbContext(typeof(GalleryDb))]
    partial class GalleryDbModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
            modelBuilder
                .HasAnnotation("ProductVersion", "1.1.1");

            modelBuilder.Entity("ExClient.Models.SavedGalleryModel", b =>
            {
                b.Property<long>("GalleryId");

                b.Property<long>("saved");

                b.Property<byte[]>("ThumbData");

                b.HasKey("GalleryId");

                b.HasIndex("GalleryId")
                    .IsUnique();

                b.ToTable("SavedSet");
            });

            modelBuilder.Entity("ExClient.Models.GalleryModel", b =>
            {
                b.Property<long>("Id");

                b.Property<string>("ArchiverKey");

                b.Property<bool>("Available");

                b.Property<uint>("Category");

                b.Property<bool>("Expunged");

                b.Property<long>("FileSize");

                b.Property<long>("posted");

                b.Property<double>("Rating");

                b.Property<int>("RecordCount");

                b.Property<string>("Tags");

                b.Property<string>("ThumbUri");

                b.Property<string>("Title");

                b.Property<string>("TitleJpn");

                b.Property<ulong>("Token");

                b.Property<string>("Uploader");

                b.HasKey("Id");

                b.ToTable("GallerySet");
            });

            modelBuilder.Entity("ExClient.Models.ImageModel", b =>
            {
                b.Property<int>("PageId");

                b.Property<long>("OwnerId");

                b.Property<string>("FileName");

                b.Property<ulong>("ImageKey");

                b.Property<bool>("OriginalLoaded");

                b.HasKey("PageId", "OwnerId");

                b.HasIndex("OwnerId");

                b.ToTable("ImageSet");
            });

            modelBuilder.Entity("ExClient.Models.SavedGalleryModel", b =>
            {
                b.HasOne("ExClient.Models.GalleryModel", "Gallery")
                    .WithOne()
                    .HasForeignKey("ExClient.Models.SavedGalleryModel", "GalleryId")
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity("ExClient.Models.ImageModel", b =>
            {
                b.HasOne("ExClient.Models.GalleryModel", "Owner")
                    .WithMany("Images")
                    .HasForeignKey("OwnerId")
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
