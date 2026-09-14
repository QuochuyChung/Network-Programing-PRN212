CREATE TABLE "Users" (
    "Id" SERIAL PRIMARY KEY,
    "Username" VARCHAR(50) UNIQUE NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE "Groups" (
    "Id" SERIAL PRIMARY KEY,
    "Name" VARCHAR(100) NOT NULL,
    "CreatedAt" TIMESTAMP NOT NULL DEFAULT NOW()
);

CREATE TABLE "GroupMembers" (
    "GroupId" INT NOT NULL REFERENCES "Groups"("Id"),
    "UserId" INT NOT NULL REFERENCES "Users"("Id"),
    "JoinedAt" TIMESTAMP NOT NULL DEFAULT NOW(),
    PRIMARY KEY ("GroupId", "UserId")
);

CREATE TABLE "Messages" (
    "Id" SERIAL PRIMARY KEY,
    "GroupId" INT NOT NULL REFERENCES "Groups"("Id"),
    "Sender" VARCHAR(50) NOT NULL,
    "Content" TEXT NOT NULL,
    "SentAt" TIMESTAMP NOT NULL DEFAULT NOW()
);