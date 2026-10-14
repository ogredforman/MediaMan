-- MediaMan database fixture: the schema at migration 20261011224526_AddMediaFileAddedAt, with UpgradeFixtureData.
-- Made by `npm run fixture:database` (UpgradeFixtureWriter). Frozen: never edit or regenerate it.
CREATE TABLE "Artwork" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Artwork" PRIMARY KEY,
    "TitleId" TEXT NULL,
    "SeasonId" TEXT NULL,
    "EpisodeId" TEXT NULL,
    "Type" TEXT NOT NULL,
    "Url" TEXT NOT NULL,
    "LocalPath" TEXT NULL,
    "DominantColor" TEXT NULL,
    CONSTRAINT "CK_Artwork_ExactlyOneOwner" CHECK (("TitleId" IS NOT NULL) + ("SeasonId" IS NOT NULL) + ("EpisodeId" IS NOT NULL) = 1),
    CONSTRAINT "FK_Artwork_Episodes_EpisodeId" FOREIGN KEY ("EpisodeId") REFERENCES "Episodes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Artwork_Seasons_SeasonId" FOREIGN KEY ("SeasonId") REFERENCES "Seasons" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Artwork_Titles_TitleId" FOREIGN KEY ("TitleId") REFERENCES "Titles" ("Id") ON DELETE CASCADE
);
CREATE TABLE "AuditEvents" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AuditEvents" PRIMARY KEY,
    "UserId" TEXT NULL,
    "Action" TEXT NOT NULL,
    "Target" TEXT NOT NULL,
    "At" INTEGER NOT NULL,
    CONSTRAINT "FK_AuditEvents_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);
CREATE TABLE "DownloadFiles" (
    "DownloadId" TEXT NOT NULL,
    "Index" INTEGER NOT NULL,
    "Path" TEXT NOT NULL,
    "SizeBytes" INTEGER NOT NULL,
    "Priority" TEXT NOT NULL,
    "Skipped" INTEGER NOT NULL,
    "Dangerous" INTEGER NOT NULL,
    CONSTRAINT "PK_DownloadFiles" PRIMARY KEY ("DownloadId", "Index"),
    CONSTRAINT "FK_DownloadFiles_Downloads_DownloadId" FOREIGN KEY ("DownloadId") REFERENCES "Downloads" ("Id") ON DELETE CASCADE
);
CREATE TABLE "DownloadRequests" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DownloadRequests" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "TitleRef" TEXT NOT NULL,
    "Release" TEXT NOT NULL,
    "Filter" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "DecidedBy" TEXT NULL,
    CONSTRAINT "FK_DownloadRequests_Users_DecidedBy" FOREIGN KEY ("DecidedBy") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_DownloadRequests_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Downloads" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Downloads" PRIMARY KEY,
    "InfoHash" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "State" TEXT NOT NULL,
    "Progress" REAL NOT NULL,
    "SavePath" TEXT NOT NULL,
    "TargetTitleId" TEXT NULL,
    "RequestedBy" TEXT NOT NULL,
    "SeedRatioLimit" REAL NULL,
    "SeedTimeLimit" INTEGER NULL,
    CONSTRAINT "FK_Downloads_Titles_TargetTitleId" FOREIGN KEY ("TargetTitleId") REFERENCES "Titles" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Downloads_Users_RequestedBy" FOREIGN KEY ("RequestedBy") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "Episodes" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Episodes" PRIMARY KEY,
    "SeasonId" TEXT NOT NULL,
    "Number" INTEGER NOT NULL,
    "AbsoluteNumber" INTEGER NULL,
    "Name" TEXT NULL,
    "Overview" TEXT NULL,
    "AirDate" TEXT NULL,
    CONSTRAINT "FK_Episodes_Seasons_SeasonId" FOREIGN KEY ("SeasonId") REFERENCES "Seasons" ("Id") ON DELETE CASCADE
);
CREATE TABLE "FileLinks" (
    "MediaFileId" TEXT NOT NULL,
    "PlayableId" TEXT NOT NULL,
    "Part" INTEGER NOT NULL,
    CONSTRAINT "PK_FileLinks" PRIMARY KEY ("MediaFileId", "PlayableId"),
    CONSTRAINT "FK_FileLinks_MediaFiles_MediaFileId" FOREIGN KEY ("MediaFileId") REFERENCES "MediaFiles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_FileLinks_Playables_PlayableId" FOREIGN KEY ("PlayableId") REFERENCES "Playables" ("Id") ON DELETE CASCADE
);
CREATE TABLE "IndexerDefinitions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_IndexerDefinitions" PRIMARY KEY,
    "Key" TEXT NOT NULL,
    "Version" TEXT NOT NULL,
    "Yaml" TEXT NOT NULL,
    "SourceUrl" TEXT NOT NULL
);
CREATE TABLE "Indexers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Indexers" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "DefinitionId" TEXT NULL,
    "Settings" TEXT NOT NULL,
    "Enabled" INTEGER NOT NULL,
    "UseFlareSolverr" INTEGER NOT NULL,
    "Health" TEXT NULL,
    CONSTRAINT "FK_Indexers_IndexerDefinitions_DefinitionId" FOREIGN KEY ("DefinitionId") REFERENCES "IndexerDefinitions" ("Id") ON DELETE SET NULL
);
CREATE TABLE "Invites" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Invites" PRIMARY KEY,
    "TokenHash" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    "CreatedBy" TEXT NOT NULL,
    "ExpiresAt" INTEGER NOT NULL,
    "MaxUses" INTEGER NOT NULL,
    "Uses" INTEGER NOT NULL,
    CONSTRAINT "FK_Invites_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "Roles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Invites_Users_CreatedBy" FOREIGN KEY ("CreatedBy") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Libraries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Libraries" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "Kind" TEXT NOT NULL,
    "ContentType" TEXT NOT NULL,
    "RootPaths" TEXT NOT NULL,
    "ScanIntervalMinutes" INTEGER NOT NULL
, "LastScanFinishedAt" INTEGER NULL, "ScanStartedAt" INTEGER NULL);
CREATE TABLE "MatchOverrides" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_MatchOverrides" PRIMARY KEY,
    "LibraryId" TEXT NOT NULL,
    "Path" TEXT NOT NULL,
    "TitleRef" TEXT NOT NULL,
    "Kind" TEXT NOT NULL,
    "Season" INTEGER NULL,
    "Episodes" TEXT NOT NULL,
    "AbsoluteEpisode" INTEGER NULL,
    "CreatedAt" INTEGER NOT NULL,
    CONSTRAINT "FK_MatchOverrides_Libraries_LibraryId" FOREIGN KEY ("LibraryId") REFERENCES "Libraries" ("Id") ON DELETE CASCADE
);
CREATE TABLE "MatchReviews" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_MatchReviews" PRIMARY KEY,
    "MediaFileId" TEXT NOT NULL,
    "Candidates" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    CONSTRAINT "FK_MatchReviews_MediaFiles_MediaFileId" FOREIGN KEY ("MediaFileId") REFERENCES "MediaFiles" ("Id") ON DELETE CASCADE
);
CREATE TABLE "MediaFiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_MediaFiles" PRIMARY KEY,
    "LibraryId" TEXT NOT NULL,
    "Path" TEXT NOT NULL,
    "SizeBytes" INTEGER NOT NULL,
    "Mtime" INTEGER NOT NULL,
    "Probe" TEXT NULL,
    "Parsed" TEXT NULL,
    "Status" TEXT NOT NULL, "AttentionReason" TEXT NULL, "MissingSince" INTEGER NULL, "AddedAt" INTEGER NOT NULL DEFAULT 0,
    CONSTRAINT "FK_MediaFiles_Libraries_LibraryId" FOREIGN KEY ("LibraryId") REFERENCES "Libraries" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Playables" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Playables" PRIMARY KEY,
    "Kind" TEXT NOT NULL,
    "TitleId" TEXT NULL,
    "EpisodeId" TEXT NULL,
    CONSTRAINT "CK_Playables_KindMatchesOwner" CHECK (("Kind" = 'Movie' AND "TitleId" IS NOT NULL AND "EpisodeId" IS NULL) OR ("Kind" = 'Episode' AND "EpisodeId" IS NOT NULL AND "TitleId" IS NULL)),
    CONSTRAINT "FK_Playables_Episodes_EpisodeId" FOREIGN KEY ("EpisodeId") REFERENCES "Episodes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Playables_Titles_TitleId" FOREIGN KEY ("TitleId") REFERENCES "Titles" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Profiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Profiles" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Avatar" TEXT NULL,
    "PinHash" TEXT NULL,
    "MaxRating" TEXT NULL,
    "AudioLangPrefs" TEXT NOT NULL,
    "SubtitleLangPrefs" TEXT NOT NULL,
    CONSTRAINT "FK_Profiles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Roles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Roles" PRIMARY KEY,
    "Name" TEXT COLLATE NOCASE NOT NULL,
    "Permissions" TEXT NOT NULL,
    "IsPreset" INTEGER NOT NULL
);
CREATE TABLE "SavedQualityProfiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SavedQualityProfiles" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Filter" TEXT NOT NULL,
    CONSTRAINT "FK_SavedQualityProfiles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Seasons" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Seasons" PRIMARY KEY,
    "TitleId" TEXT NOT NULL,
    "Number" INTEGER NOT NULL,
    "Name" TEXT NULL,
    "Overview" TEXT NULL,
    CONSTRAINT "FK_Seasons_Titles_TitleId" FOREIGN KEY ("TitleId") REFERENCES "Titles" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Sessions" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Sessions" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "DeviceName" TEXT NOT NULL,
    "RefreshTokenHash" TEXT NOT NULL,
    "CreatedAt" INTEGER NOT NULL,
    "LastSeenAt" INTEGER NOT NULL,
    "ExpiresAt" INTEGER NOT NULL,
    "Revoked" INTEGER NOT NULL, "ActiveProfileId" TEXT NULL REFERENCES "Profiles" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Sessions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);
CREATE TABLE "Settings" (
    "Key" TEXT NOT NULL CONSTRAINT "PK_Settings" PRIMARY KEY,
    "Value" TEXT NOT NULL
);
CREATE TABLE "Titles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Titles" PRIMARY KEY,
    "Kind" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "Year" INTEGER NULL,
    "Overview" TEXT NULL,
    "Genres" TEXT NOT NULL,
    "Rating" REAL NULL,
    "RuntimeMinutes" INTEGER NULL,
    "ExternalIds_Tmdb" TEXT NULL,
    "ExternalIds_Imdb" TEXT NULL,
    "ExternalIds_Tvdb" TEXT NULL,
    "ExternalIds_AniList" TEXT NULL
);
CREATE TABLE "Users" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
    "Username" TEXT COLLATE NOCASE NOT NULL,
    "PasswordHash" TEXT NOT NULL,
    "RoleId" TEXT NOT NULL,
    "IsSuperadmin" INTEGER NOT NULL,
    "TotpSecret" TEXT NULL,
    "Disabled" INTEGER NOT NULL,
    "CreatedAt" INTEGER NOT NULL,
    CONSTRAINT "FK_Users_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "Roles" ("Id") ON DELETE RESTRICT
);
CREATE TABLE "VersionPreferences" (
    "ProfileId" TEXT NOT NULL,
    "TitleId" TEXT NOT NULL,
    "MediaFileId" TEXT NULL,
    "Resolution" TEXT NULL,
    "HdrFormat" TEXT NULL,
    "UpdatedAt" INTEGER NOT NULL,
    CONSTRAINT "PK_VersionPreferences" PRIMARY KEY ("ProfileId", "TitleId"),
    CONSTRAINT "FK_VersionPreferences_MediaFiles_MediaFileId" FOREIGN KEY ("MediaFileId") REFERENCES "MediaFiles" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_VersionPreferences_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "Profiles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_VersionPreferences_Titles_TitleId" FOREIGN KEY ("TitleId") REFERENCES "Titles" ("Id") ON DELETE CASCADE
);
CREATE TABLE "VpnConfig" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_VpnConfig" PRIMARY KEY,
    "Mode" TEXT NOT NULL,
    "InterfaceId" TEXT NULL,
    "SocksHost" TEXT NULL,
    "SocksPort" INTEGER NULL,
    "SocksUsername" TEXT NULL,
    "SocksPassword" TEXT NULL,
    "LeakCheckIntervalSeconds" INTEGER NOT NULL,
    CONSTRAINT "CK_VpnConfig_SingleRow" CHECK ("Id" = 'vpn')
);
CREATE TABLE "WatchStates" (
    "ProfileId" TEXT NOT NULL,
    "PlayableId" TEXT NOT NULL,
    "PositionSeconds" REAL NOT NULL,
    "DurationSeconds" REAL NOT NULL,
    "Watched" INTEGER NOT NULL,
    "LastWatchedAt" INTEGER NOT NULL,
    CONSTRAINT "PK_WatchStates" PRIMARY KEY ("ProfileId", "PlayableId"),
    CONSTRAINT "FK_WatchStates_Playables_PlayableId" FOREIGN KEY ("PlayableId") REFERENCES "Playables" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_WatchStates_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "Profiles" ("Id") ON DELETE CASCADE
);
CREATE TABLE "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);
CREATE INDEX "IX_Artwork_EpisodeId" ON "Artwork" ("EpisodeId");
CREATE INDEX "IX_Artwork_SeasonId" ON "Artwork" ("SeasonId");
CREATE INDEX "IX_Artwork_TitleId" ON "Artwork" ("TitleId");
CREATE INDEX "IX_AuditEvents_At" ON "AuditEvents" ("At");
CREATE INDEX "IX_AuditEvents_UserId" ON "AuditEvents" ("UserId");
CREATE INDEX "IX_DownloadRequests_DecidedBy" ON "DownloadRequests" ("DecidedBy");
CREATE INDEX "IX_DownloadRequests_Status" ON "DownloadRequests" ("Status");
CREATE INDEX "IX_DownloadRequests_UserId" ON "DownloadRequests" ("UserId");
CREATE UNIQUE INDEX "IX_Downloads_InfoHash" ON "Downloads" ("InfoHash");
CREATE INDEX "IX_Downloads_RequestedBy" ON "Downloads" ("RequestedBy");
CREATE INDEX "IX_Downloads_TargetTitleId" ON "Downloads" ("TargetTitleId");
CREATE UNIQUE INDEX "IX_Episodes_SeasonId_Number" ON "Episodes" ("SeasonId", "Number");
CREATE INDEX "IX_FileLinks_PlayableId" ON "FileLinks" ("PlayableId");
CREATE UNIQUE INDEX "IX_IndexerDefinitions_Key" ON "IndexerDefinitions" ("Key");
CREATE INDEX "IX_Indexers_DefinitionId" ON "Indexers" ("DefinitionId");
CREATE INDEX "IX_Invites_CreatedBy" ON "Invites" ("CreatedBy");
CREATE INDEX "IX_Invites_RoleId" ON "Invites" ("RoleId");
CREATE UNIQUE INDEX "IX_Invites_TokenHash" ON "Invites" ("TokenHash");
CREATE UNIQUE INDEX "IX_MatchOverrides_LibraryId_Path" ON "MatchOverrides" ("LibraryId", "Path");
CREATE UNIQUE INDEX "IX_MatchReviews_MediaFileId" ON "MatchReviews" ("MediaFileId");
CREATE INDEX "IX_MediaFiles_LibraryId" ON "MediaFiles" ("LibraryId");
CREATE UNIQUE INDEX "IX_MediaFiles_Path" ON "MediaFiles" ("Path");
CREATE INDEX "IX_MediaFiles_Status" ON "MediaFiles" ("Status");
CREATE UNIQUE INDEX "IX_Playables_EpisodeId" ON "Playables" ("EpisodeId");
CREATE UNIQUE INDEX "IX_Playables_TitleId" ON "Playables" ("TitleId");
CREATE UNIQUE INDEX "IX_Profiles_UserId_Name" ON "Profiles" ("UserId", "Name" COLLATE NOCASE);
CREATE UNIQUE INDEX "IX_Roles_Name" ON "Roles" ("Name");
CREATE UNIQUE INDEX "IX_SavedQualityProfiles_UserId_Name" ON "SavedQualityProfiles" ("UserId", "Name");
CREATE UNIQUE INDEX "IX_Seasons_TitleId_Number" ON "Seasons" ("TitleId", "Number");
CREATE INDEX "IX_Sessions_ActiveProfileId" ON "Sessions" ("ActiveProfileId");
CREATE UNIQUE INDEX "IX_Sessions_RefreshTokenHash" ON "Sessions" ("RefreshTokenHash");
CREATE INDEX "IX_Sessions_UserId" ON "Sessions" ("UserId");
CREATE INDEX "IX_Titles_ExternalIds_AniList" ON "Titles" ("ExternalIds_AniList");
CREATE INDEX "IX_Titles_ExternalIds_Imdb" ON "Titles" ("ExternalIds_Imdb");
CREATE INDEX "IX_Titles_ExternalIds_Tmdb" ON "Titles" ("ExternalIds_Tmdb");
CREATE INDEX "IX_Titles_ExternalIds_Tvdb" ON "Titles" ("ExternalIds_Tvdb");
CREATE UNIQUE INDEX "IX_Users_IsSuperadmin" ON "Users" ("IsSuperadmin") WHERE "IsSuperadmin" = 1;
CREATE INDEX "IX_Users_RoleId" ON "Users" ("RoleId");
CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");
CREATE INDEX "IX_VersionPreferences_MediaFileId" ON "VersionPreferences" ("MediaFileId");
CREATE INDEX "IX_VersionPreferences_TitleId" ON "VersionPreferences" ("TitleId");
CREATE INDEX "IX_WatchStates_PlayableId" ON "WatchStates" ("PlayableId");
CREATE INDEX "IX_WatchStates_ProfileId_LastWatchedAt" ON "WatchStates" ("ProfileId", "LastWatchedAt");
INSERT INTO "Artwork" ("Id", "TitleId", "SeasonId", "EpisodeId", "Type", "Url", "LocalPath", "DominantColor") VALUES ('0199D0C0-0000-7000-8000-000000000020', '0199D0C0-0000-7000-8000-000000000014', NULL, NULL, 'Poster', 'https://image.tmdb.org/t/p/original/matrix.jpg', 'artwork/poster.jpg', '#1A2B3C');
INSERT INTO "Artwork" ("Id", "TitleId", "SeasonId", "EpisodeId", "Type", "Url", "LocalPath", "DominantColor") VALUES ('0199D0C0-0000-7000-8000-000000000021', NULL, NULL, '0199D0C0-0000-7000-8000-000000000017', 'Still', 'https://image.tmdb.org/t/p/original/pilot.jpg', NULL, NULL);
INSERT INTO "AuditEvents" ("Id", "UserId", "Action", "Target", "At") VALUES ('0199D0C0-0000-7000-8000-000000000025', '0199D0C0-0000-7000-8000-000000000003', 'setup.completed', 'server', 1790856000000);
INSERT INTO "Episodes" ("Id", "SeasonId", "Number", "AbsoluteNumber", "Name", "Overview", "AirDate") VALUES ('0199D0C0-0000-7000-8000-000000000017', '0199D0C0-0000-7000-8000-000000000016', 1, NULL, 'Pilot', NULL, '2008-01-20');
INSERT INTO "FileLinks" ("MediaFileId", "PlayableId", "Part") VALUES ('0199D0C0-0000-7000-8000-000000000009', '0199D0C0-0000-7000-8000-000000000018', 1);
INSERT INTO "FileLinks" ("MediaFileId", "PlayableId", "Part") VALUES ('0199D0C0-0000-7000-8000-000000000010', '0199D0C0-0000-7000-8000-000000000018', 1);
INSERT INTO "FileLinks" ("MediaFileId", "PlayableId", "Part") VALUES ('0199D0C0-0000-7000-8000-000000000011', '0199D0C0-0000-7000-8000-000000000019', 1);
INSERT INTO "Invites" ("Id", "TokenHash", "RoleId", "CreatedBy", "ExpiresAt", "MaxUses", "Uses") VALUES ('0199D0C0-0000-7000-8000-000000000024', 'invite-token-hash', '0199D0C0-0000-7000-8000-000000000002', '0199D0C0-0000-7000-8000-000000000003', 1791460800000, 2, 1);
INSERT INTO "Libraries" ("Id", "Name", "Kind", "ContentType", "RootPaths", "ScanIntervalMinutes", "LastScanFinishedAt", "ScanStartedAt") VALUES ('0199D0C0-0000-7000-8000-000000000008', 'Media', 'External', 'Mixed', '["/media/Movies","/media/Shows"]', 60, 1790856000000, NULL);
INSERT INTO "MatchOverrides" ("Id", "LibraryId", "Path", "TitleRef", "Kind", "Season", "Episodes", "AbsoluteEpisode", "CreatedAt") VALUES ('0199D0C0-0000-7000-8000-000000000023', '0199D0C0-0000-7000-8000-000000000008', '/media/Movies/The Matrix (1999)/The Matrix (1999) 2160p.mkv', 'tmdb:movie/603', 'Movie', NULL, '[]', NULL, 1790856000000);
INSERT INTO "MatchReviews" ("Id", "MediaFileId", "Candidates", "Reason") VALUES ('0199D0C0-0000-7000-8000-000000000022', '0199D0C0-0000-7000-8000-000000000012', '[{"TitleRef":"tmdb:movie/1","Score":0.7,"Kind":"Movie","Name":"Unknown Thing","Year":2020,"PosterUrl":null}]', 'ambiguous');
INSERT INTO "MediaFiles" ("Id", "LibraryId", "Path", "SizeBytes", "Mtime", "Probe", "Parsed", "Status", "AttentionReason", "MissingSince", "AddedAt") VALUES ('0199D0C0-0000-7000-8000-000000000009', '0199D0C0-0000-7000-8000-000000000008', '/media/Movies/The Matrix (1999)/The Matrix (1999) 2160p.mkv', 40000000000, 1789992000000, '{"Container":"matroska","Duration":"02:16:00","HdrFormat":"HDR10","Streams":[{"Index":0,"Type":"Video","Codec":"hevc","Language":null,"Title":null,"IsDefault":true,"IsForced":false,"Width":3840,"Height":2160,"Channels":null,"BitRate":null},{"Index":1,"Type":"Audio","Codec":"truehd","Language":"eng","Title":null,"IsDefault":false,"IsForced":false,"Width":null,"Height":null,"Channels":8,"BitRate":null}]}', '{"Title":"The Matrix","Year":1999,"Season":null,"Episodes":[],"AbsoluteEpisode":null,"Resolution":"P2160","Source":null,"Codec":null,"HdrFormat":"HDR10","Audio":null,"Group":null}', 'Matched', NULL, NULL, 1790078400000);
INSERT INTO "MediaFiles" ("Id", "LibraryId", "Path", "SizeBytes", "Mtime", "Probe", "Parsed", "Status", "AttentionReason", "MissingSince", "AddedAt") VALUES ('0199D0C0-0000-7000-8000-000000000010', '0199D0C0-0000-7000-8000-000000000008', '/media/Movies/The Matrix (1999)/The Matrix (1999) 720p.mkv', 4000000000, 1790164800000, NULL, '{"Title":"The Matrix","Year":1999,"Season":null,"Episodes":[],"AbsoluteEpisode":null,"Resolution":"P720","Source":null,"Codec":null,"HdrFormat":null,"Audio":null,"Group":null}', 'Matched', NULL, NULL, 1790164800000);
INSERT INTO "MediaFiles" ("Id", "LibraryId", "Path", "SizeBytes", "Mtime", "Probe", "Parsed", "Status", "AttentionReason", "MissingSince", "AddedAt") VALUES ('0199D0C0-0000-7000-8000-000000000011', '0199D0C0-0000-7000-8000-000000000008', '/media/Shows/Breaking Bad/Season 01/Breaking Bad S01E01.mkv', 2000000000, 1790424000000, NULL, '{"Title":"Breaking Bad","Year":null,"Season":1,"Episodes":[1],"AbsoluteEpisode":null,"Resolution":null,"Source":null,"Codec":null,"HdrFormat":null,"Audio":null,"Group":null}', 'Matched', NULL, NULL, 1790424000000);
INSERT INTO "MediaFiles" ("Id", "LibraryId", "Path", "SizeBytes", "Mtime", "Probe", "Parsed", "Status", "AttentionReason", "MissingSince", "AddedAt") VALUES ('0199D0C0-0000-7000-8000-000000000012', '0199D0C0-0000-7000-8000-000000000008', '/media/Movies/Unknown Thing (2020).mkv', 1000, 1790683200000, NULL, NULL, 'NeedsAttention', 'ambiguous', NULL, 1790683200000);
INSERT INTO "MediaFiles" ("Id", "LibraryId", "Path", "SizeBytes", "Mtime", "Probe", "Parsed", "Status", "AttentionReason", "MissingSince", "AddedAt") VALUES ('0199D0C0-0000-7000-8000-000000000013', '0199D0C0-0000-7000-8000-000000000008', '/media/Movies/Gone (2001).mkv', 500, 1788264000000, NULL, NULL, 'Missing', NULL, 1790769600000, 1788264000000);
INSERT INTO "Playables" ("Id", "Kind", "TitleId", "EpisodeId") VALUES ('0199D0C0-0000-7000-8000-000000000018', 'Movie', '0199D0C0-0000-7000-8000-000000000014', NULL);
INSERT INTO "Playables" ("Id", "Kind", "TitleId", "EpisodeId") VALUES ('0199D0C0-0000-7000-8000-000000000019', 'Episode', NULL, '0199D0C0-0000-7000-8000-000000000017');
INSERT INTO "Profiles" ("Id", "UserId", "Name", "Avatar", "PinHash", "MaxRating", "AudioLangPrefs", "SubtitleLangPrefs") VALUES ('0199D0C0-0000-7000-8000-000000000005', '0199D0C0-0000-7000-8000-000000000003', 'Owner', 'purple', NULL, NULL, '["ja","en"]', '["en"]');
INSERT INTO "Profiles" ("Id", "UserId", "Name", "Avatar", "PinHash", "MaxRating", "AudioLangPrefs", "SubtitleLangPrefs") VALUES ('0199D0C0-0000-7000-8000-000000000006', '0199D0C0-0000-7000-8000-000000000004', 'Viewer', NULL, NULL, NULL, '[]', '[]');
INSERT INTO "Roles" ("Id", "Name", "Permissions", "IsPreset") VALUES ('0199D0C0-0000-7000-8000-000000000001', 'Admin', '["library.view","server.control"]', 1);
INSERT INTO "Roles" ("Id", "Name", "Permissions", "IsPreset") VALUES ('0199D0C0-0000-7000-8000-000000000002', 'Viewer', '["library.view"]', 1);
INSERT INTO "Seasons" ("Id", "TitleId", "Number", "Name", "Overview") VALUES ('0199D0C0-0000-7000-8000-000000000016', '0199D0C0-0000-7000-8000-000000000015', 1, 'Season 1', NULL);
INSERT INTO "Sessions" ("Id", "UserId", "DeviceName", "RefreshTokenHash", "CreatedAt", "LastSeenAt", "ExpiresAt", "Revoked", "ActiveProfileId") VALUES ('0199D0C0-0000-7000-8000-000000000007', '0199D0C0-0000-7000-8000-000000000003', 'Desktop', '5f0c1a6e3b9d4c2a8e7f6d5c4b3a291807f6e5d4c3b2a1908f7e6d5c4b3a2918', 1790856000000, 1790859600000, 1793448000000, 0, '0199D0C0-0000-7000-8000-000000000005');
INSERT INTO "Settings" ("Key", "Value") VALUES ('metadata.tmdb.accessToken', 'v1:dQnKIgt04sTpOJJdSsppPmliStq7EEwqT5WXEQaUifwR6m0qKAh8a3G9zsSkHF5PqFv4DjqiHKI8aQ==');
INSERT INTO "Settings" ("Key", "Value") VALUES ('server.mode', 'server');
INSERT INTO "Titles" ("Id", "Kind", "Name", "Year", "Overview", "Genres", "Rating", "RuntimeMinutes", "ExternalIds_Tmdb", "ExternalIds_Imdb", "ExternalIds_Tvdb", "ExternalIds_AniList") VALUES ('0199D0C0-0000-7000-8000-000000000014', 'Movie', 'The Matrix', 1999, 'A hacker learns the truth.', '["Action","Science Fiction"]', 8.2, 136, 'movie/603', 'tt0133093', NULL, NULL);
INSERT INTO "Titles" ("Id", "Kind", "Name", "Year", "Overview", "Genres", "Rating", "RuntimeMinutes", "ExternalIds_Tmdb", "ExternalIds_Imdb", "ExternalIds_Tvdb", "ExternalIds_AniList") VALUES ('0199D0C0-0000-7000-8000-000000000015', 'Show', 'Breaking Bad', 2008, NULL, '["Drama"]', NULL, NULL, 'tv/1396', NULL, NULL, NULL);
INSERT INTO "Users" ("Id", "Username", "PasswordHash", "RoleId", "IsSuperadmin", "TotpSecret", "Disabled", "CreatedAt") VALUES ('0199D0C0-0000-7000-8000-000000000003', 'owner', '$argon2id$v=19$m=19456,t=2,p=1$Zml4dHVyZS1zYWx0LTAx$Zml4dHVyZS1oYXNoLW5vdC1hLXJlYWwtcGFzc3dvcmQ', '0199D0C0-0000-7000-8000-000000000001', 1, 'v1:wBIv/UcfLyrddtn05dZyP7ftnJO43Pg3CIGLvneO03yd/+dzACv6nuVv5PU=', 0, 1790856000000);
INSERT INTO "Users" ("Id", "Username", "PasswordHash", "RoleId", "IsSuperadmin", "TotpSecret", "Disabled", "CreatedAt") VALUES ('0199D0C0-0000-7000-8000-000000000004', 'viewer', '$argon2id$v=19$m=19456,t=2,p=1$Zml4dHVyZS1zYWx0LTAx$Zml4dHVyZS1oYXNoLW5vdC1hLXJlYWwtcGFzc3dvcmQ', '0199D0C0-0000-7000-8000-000000000002', 0, NULL, 1, 1790856000000);
INSERT INTO "VersionPreferences" ("ProfileId", "TitleId", "MediaFileId", "Resolution", "HdrFormat", "UpdatedAt") VALUES ('0199D0C0-0000-7000-8000-000000000005', '0199D0C0-0000-7000-8000-000000000014', '0199D0C0-0000-7000-8000-000000000010', '720p', NULL, 1790856000000);
INSERT INTO "WatchStates" ("ProfileId", "PlayableId", "PositionSeconds", "DurationSeconds", "Watched", "LastWatchedAt") VALUES ('0199D0C0-0000-7000-8000-000000000005', '0199D0C0-0000-7000-8000-000000000018', 1234.5, 8160.0, 0, 1790856000000);
INSERT INTO "WatchStates" ("ProfileId", "PlayableId", "PositionSeconds", "DurationSeconds", "Watched", "LastWatchedAt") VALUES ('0199D0C0-0000-7000-8000-000000000005', '0199D0C0-0000-7000-8000-000000000019', 0.0, 3480.0, 1, 1790850600000);
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20261005043527_Initial', '10.0.12');
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20261006101246_AddMediaFileScanState', '10.0.12');
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20261008084532_AddSessionActiveProfile', '10.0.12');
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20261009175531_AddLibraryScanTimes', '10.0.12');
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20261009230057_AddMatchOverrides', '10.0.12');
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20261010024327_AddVersionPreferences', '10.0.12');
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ('20261011224526_AddMediaFileAddedAt', '10.0.12');
