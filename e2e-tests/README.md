# ImageGc E2E Tests

End-to-end testing for the ImageGc application using Playwright and TypeScript.

## Setup

1. Install dependencies:
```bash
cd e2e-tests
npm install
```

2. Install Playwright browsers:
```bash
npm run install
```

## Running Tests

### Run all tests:
```bash
npm test
```

### Run tests in headed mode (see the browser):
```bash
npm run test:headed
```

### Run tests in debug mode:
```bash
npm run test:debug
```

### Run only Chromium tests:
```bash
npm run test:chromium
```

### Run mobile viewport tests:
```bash
npm run test:mobile
```

### View test report:
```bash
npm run show-report
```

## Test Structure

```
e2e-tests/
├── tests/
│   ├── home.spec.ts         # Home page functionality
│   └── diagnostics.spec.ts  # Diagnostics page tests
├── playwright.config.ts     # Playwright configuration
└── package.json            # Dependencies and scripts
```

## Test Coverage

### Home Page (`home.spec.ts`)
- Page loads successfully
- File upload section is visible
- Description length slider is present
- Responsive layout (mobile/desktop)
- Navigation functionality
- Image upload workflow
- File validation

### Diagnostics Page (`diagnostics.spec.ts`)
- Page loads successfully
- Health status indicators display
- Refresh functionality
- Multiple health check sections
- Responsive design
- API health endpoint returns 200 OK
- Valid JSON response from health endpoint

## Configuration

The tests are configured to:
- Run against `http://localhost:5000`
- Automatically start the server before tests (`dotnet run` in Server directory)
- Test on both desktop (Chromium) and mobile (Pixel 5) viewports
- Capture screenshots on failure
- Record video on failure
- Generate HTML reports

## CI/CD Integration

The tests can be run in CI/CD pipelines. They will:
- Use 2 retries on failures
- Run sequentially (not in parallel)
- Fail the build if `test.only` is accidentally left in code

## Notes

- Tests use TypeScript for type safety
- Only Chromium is used for testing (as per requirements)
- Mobile viewport testing uses Pixel 5 device emulation
- Server must be running on port 5000 (handled automatically by webServer config)
- Make sure API keys are configured in `Server/appsettings.Development.json` for full test coverage
