# TADERATCS Playwright MCP Server

A comprehensive Playwright-based Model Context Protocol (MCP) server designed specifically for testing the TADERATCS (TSA Airport Access Control System) enrollment form.

## Features

### 🌐 Multi-Browser Support
- **Chrome/Chromium**: Full automation with debugging capabilities
- **Firefox**: Cross-browser compatibility testing  
- **WebKit/Safari**: Complete browser coverage for kiosk deployment

### 🎯 TADERATCS-Specific Testing
- **Data-TestID Integration**: Native support for data-testid selectors
- **Enrollment Form Testing**: Pre-built test cases for enrollment workflow
- **Business Rules Validation**: SSN, SubProgram, and cross-tab validation
- **LocalStorage Testing**: Auto-save and recovery functionality validation

### 🔧 Core Testing Tools

#### Browser Management
- `LaunchBrowser`: Start browser with debugging capabilities
- `CloseBrowser`: Clean shutdown and resource cleanup
- `NavigateToUrl`: Page navigation with error handling

#### Element Interaction  
- `FillField`: Form field input with data-testid support
- `ClickElement`: Element clicking with automatic waiting
- `SelectOption`: Dropdown and select element interaction
- `ValidateElement`: Element state validation (visible, enabled, text content)

#### Advanced Testing
- `TakeScreenshot`: Visual debugging and test documentation
- `WaitForElement`: Smart waiting for dynamic content
- `ExecuteJavaScript`: Custom JavaScript execution for complex scenarios
- `ExecuteTestCase`: Complete test case execution from MongoDB test collection

#### LocalStorage Testing
- `ClearLocalStorage`: Clean test environment setup
- `GetLocalStorageContents`: Auto-save functionality validation

### 🏗️ TADERATCS Integration

#### Pre-Built Test Scenarios
- **Enrollment Success Test**: Complete form filling and submission
- **LocalStorage Auto-Save Test**: Session persistence validation
- **Cross-Tab Validation**: Multi-section form validation
- **Business Rules Testing**: TSA-specific validation rules

#### Test Data Support
- Compatible with MongoDB test collection format
- Supports all test types: SUCCESS_PATH, VALIDATION_TEST, BUSINESS_RULES_TEST
- Automated test execution from stored test cases

## Usage Examples

### Basic Browser Launch
```javascript
// Launch Chrome browser with debugging
await LaunchBrowser("chrome", false, "enrollment-session")

// Navigate to enrollment form
await NavigateToUrl("http://localhost:4200/applicants", "enrollment-session")
```

### Form Field Testing
```javascript
// Fill enrollment form fields using data-testid
await FillField("personal-first-name", "John", "enrollment-session")
await FillField("personal-last-name", "Smith", "enrollment-session") 
await FillField("personal-ssn", "123-45-6789", "enrollment-session")

// Validate save button state
await ValidateElement("save-button", "enabled", null, "enrollment-session")
```

### LocalStorage Testing
```javascript
// Clear localStorage for clean test
await ClearLocalStorage("enrollment-session")

// Fill form fields
await FillField("personal-first-name", "John", "enrollment-session")

// Verify auto-save functionality
await GetLocalStorageContents("enrollment-session")
// Should show: enrollmentFormData with John's information
```

### Complete Test Execution
```javascript
// Execute full TADERATCS enrollment test
await ExecuteEnrollmentSuccessTest("enrollment-session", "http://localhost:4200")
```

## Architecture

### Service Layer
- **ToolService**: Core testing functionality and session management
- **ChromeService**: Chrome-specific browser management with debugging
- **FirefoxService**: Firefox browser automation
- **WebKitService**: Safari/WebKit browser support

### Tool Categories
- **PlaywrightTools**: Core browser automation tools
- **TADERATCSTestingTools**: TADERATCS-specific testing scenarios

### Session Management
- Multi-session support for parallel testing
- Automatic resource cleanup
- Session isolation for test reliability

## Perfect for TADERATCS Testing

This MCP server addresses all critical TADERATCS testing requirements:

✅ **Public Kiosk Testing**: Multi-browser support for various kiosk configurations  
✅ **Form Validation**: Business rules testing with TSA-specific validation  
✅ **Auto-Save Testing**: LocalStorage persistence validation  
✅ **Cross-Tab Validation**: Multi-section form validation  
✅ **File Upload Testing**: Document management workflow validation  
✅ **Performance Testing**: Network simulation and performance monitoring  
✅ **Debugging Support**: Screenshots, console monitoring, network inspection  

## Test Suite Integration

Seamlessly integrates with the MongoDB test collection containing:
- 10 comprehensive test cases
- 60-minute complete test suite
- Business rules validation
- Cross-browser compatibility testing

The server can execute any test case from the MongoDB collection, providing complete automation for the TADERATCS enrollment system testing pipeline.
