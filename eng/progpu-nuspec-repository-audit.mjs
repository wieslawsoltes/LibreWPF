function requireRepository(xml, packageId, expectedUrl, expectedCommit) {
  const repositoryElements = [...xml.matchAll(/<repository\b[^>]*\/?\s*>/g)];
  if (repositoryElements.length !== 1) {
    throw new Error(
      `Package ${packageId} nuspec expected one repository element, found ${repositoryElements.length}.`);
  }

  const attributes = new Map();
  const attributePattern = /([A-Za-z_:][A-Za-z0-9_.:-]*)\s*=\s*(["'])(.*?)\2/g;
  for (const match of repositoryElements[0][0].matchAll(attributePattern)) {
    if (attributes.has(match[1])) {
      throw new Error(`Package ${packageId} repository repeats attribute ${match[1]}.`);
    }

    attributes.set(match[1], match[3]);
  }

  const expectedAttributes = new Map([
    ["type", "git"],
    ["url", expectedUrl],
    ["commit", expectedCommit]
  ]);
  for (const [name, value] of expectedAttributes) {
    if (attributes.get(name) !== value) {
      throw new Error(
        `Package ${packageId} repository ${name} expected ${value}, found ${attributes.get(name) ?? "missing"}.`);
    }
  }
}

function expectFailure(description, callback) {
  try {
    callback();
  } catch {
    return;
  }

  throw new Error(`Expected repository audit fixture '${description}' to fail.`);
}

function runSelfTest() {
  const packageId = "Fixture.Package";
  const expectedUrl = "https://github.com/example/fixture";
  const expectedCommit = "0123456789abcdef0123456789abcdef01234567";

  requireRepository(
    `<package><metadata><repository type="git" url="${expectedUrl}" commit="${expectedCommit}" /></metadata></package>`,
    packageId,
    expectedUrl,
    expectedCommit);
  requireRepository(
    `<package><metadata><repository branch="refs/heads/main" commit="${expectedCommit}" url="${expectedUrl}" type="git" /></metadata></package>`,
    packageId,
    expectedUrl,
    expectedCommit);
  expectFailure("tampered commit", () => requireRepository(
    `<package><metadata><repository type="git" url="${expectedUrl}" commit="ffffffffffffffffffffffffffffffffffffffff" /></metadata></package>`,
    packageId,
    expectedUrl,
    expectedCommit));
  expectFailure("missing repository", () => requireRepository(
    "<package><metadata /></package>",
    packageId,
    expectedUrl,
    expectedCommit));

  console.log("ProGPU nuspec repository audit fixtures passed.");
}

try {
  const [packageId, expectedUrl, expectedCommit] = process.argv.slice(2);
  if (packageId === "--self-test") {
    runSelfTest();
  } else {
    if (!packageId || !expectedUrl || !expectedCommit) {
      throw new Error("Usage: progpu-nuspec-repository-audit.mjs <package-id> <repository-url> <commit>");
    }

    let xml = "";
    process.stdin.setEncoding("utf8");
    for await (const chunk of process.stdin) {
      xml += chunk;
    }

    requireRepository(xml, packageId, expectedUrl, expectedCommit);
  }
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exitCode = 1;
}
