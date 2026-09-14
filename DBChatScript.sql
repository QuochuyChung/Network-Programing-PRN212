CREATE TABLE "Users" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Username" VARCHAR(50) UNIQUE NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE "Groups" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name" VARCHAR(100) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE "GroupMembers" (
    "GroupId" UUID NOT NULL REFERENCES "Groups"("Id"),
    "UserId" UUID NOT NULL REFERENCES "Users"("Id"),
    "JoinedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    PRIMARY KEY ("GroupId", "UserId")
);

CREATE TABLE "Messages" (
    "Id" UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    "GroupId" UUID NOT NULL REFERENCES "Groups"("Id"),
    "Sender" VARCHAR(50) NOT NULL,
    "Content" TEXT NOT NULL,
    "SentAt" TIMESTAMP NOT NULL DEFAULT NOW()
);
