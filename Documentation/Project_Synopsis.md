# Elite Dangerous INARA Overlay - Project Synopsis

## Overview

The Elite Dangerous INARA Overlay is a sophisticated Windows overlay application designed to provide real-time trade route information for Elite Dangerous players.
The application creates an unobtrusive overlay that displays trade routes sourced from the INARA community database, helping players maximize their trading profits without leaving the game.

## Core Purpose

The overlay serves as a bridge between Elite Dangerous gameplay and the INARA community database, providing players with:
- **Real-time trade route data** directly overlaid on their game screen
- **Seamless integration** that doesn't interrupt gameplay flow
- **Advanced filtering options** to find optimal trade routes based on player preferences
- **Interactive overlay windows** that remain synchronized with the game window

## Core User Stories

### 1. Trade Route Discovery
**As a trader in Elite Dangerous**, I want to search for profitable trade routes near my current location so that I can maximize my credits per hour without alt-tabbing out of the game.

**Key Features:**
- Search by current star system location
- Filter by cargo capacity, ship size, and station preferences
- Sort results by profit margins or distance
- View both single-leg and round-trip routes

### 2. In-Game Overlay Display
**As a player**, I want trade route information displayed as an overlay on my game screen so that I can reference it while flying without minimizing the game.

**Key Features:**
- Non-intrusive overlay that follows the game window
- Auto-hide when game loses focus
- Click-through capability when not actively using the overlay
- Responsive positioning that adapts to game window size

### 3. Advanced Trade Filtering
**As an experienced trader**, I want to apply advanced filters to trade routes so that I can find routes that match my specific ship configuration and trading preferences.

**Key Features:**
- Landing pad size requirements (Small/Medium/Large)
- Maximum station distance from star
- Minimum supply/demand quantities
- PowerPlay faction preferences
- Price data age limits

### 4. Route Information Display
**As a player planning trade runs**, I want to see comprehensive route information including station details, commodity prices, and profit calculations so that I can make informed trading decisions.

**Key Features:**
- Station names, systems, and distances
- Commodity buy/sell prices with supply/demand data
- Profit per unit and total profit calculations
- Last updated timestamps for price data
- Round-trip route combinations

### 5. Global Hotkey Access
**As a player actively flying in Elite Dangerous**, I want to quickly toggle the trade route overlay using a keyboard shortcut so that I can access trading information without clicking or alt-tabbing.

**Key Features:**
- Ctrl+5 global hotkey that works even when Elite Dangerous is in focus
- Same toggle functionality as the overlay button
- System-wide hotkey registration that works from any application
- Automatic hotkey cleanup when application closes
- Graceful fallback if hotkey is already in use by another application

## Integration with Elite Dangerous

The overlay integrates with Elite Dangerous through:

### Window Management
- **Process Detection**: Automatically detects running Elite Dangerous instances
- **Window Tracking**: Monitors game window position and state
- **Overlay Positioning**: Maintains overlay position relative to game window
- **Focus Management**: Shows/hides overlay based on game window focus

### User Experience
- **Non-Intrusive Design**: Overlay doesn't interfere with game controls
- **Global Hotkey Support**: Ctrl+5 hotkey for system-wide toggle functionality
- **Contextual Visibility**: Automatically manages overlay visibility based on game state
- **Synchronized Movement**: Overlay follows game window movement and resizing
- **Performance Optimized**: Runs at 60 FPS for smooth overlay updates

## Integration with INARA

The overlay connects to INARA through:

### Data Retrieval
- **Web Scraping**: Retrieves trade route data from INARA's web interface
- **Parameter Mapping**: Translates user search criteria to INARA's form parameters
- **HTML Parsing**: Processes INARA's trade route pages to extract structured data
- **Real-time Updates**: Fetches current market data on-demand

### Search Capabilities
- **Trade Route Search**: Queries INARA's trade route database with user-specified parameters
- **Market Data**: Retrieves current commodity prices and availability
- **Station Information**: Includes station types, landing pad sizes, and distances
- **PowerPlay Integration**: Supports PowerPlay faction filtering

### Data Processing
- **Route Calculation**: Calculates profit margins and route distances
- **Result Formatting**: Presents data in user-friendly format
- **Error Handling**: Gracefully handles network issues and parsing errors
- **Fallback Data**: Provides test data when INARA is unavailable

## Technical Architecture

### Core Components
1. **Main Overlay Window**: Primary interface with toggle functionality
2. **Trade Route Search Window**: Advanced search interface with filtering options
3. **Results Overlay Window**: Displays trade route results in an overlay format
4. **INARA Communication Layer**: Handles web requests and data parsing
5. **Windows API Integration**: Manages overlay behavior and window interactions
6. **Overlay Layout Settings**: Centralized sizing/positioning constants in `EDActivityOverlay/Utils/OverlayLayoutSettings.cs`

### Adaptive Layout Model
- Overlay scaling is based on a 1920x1080 baseline and clamped ranges per window type.
- Overlay position is always clamped to the target monitor work area (not global desktop bounds).
- Window-specific limits (width/height ratios, max sizes, margins, offsets) are centralized in `OverlayLayoutSettings`.

### Key Technologies
- **WPF (.NET 8)**: Modern Windows application framework
- **HTML Agility Pack**: Web scraping and HTML parsing
- **Windows API**: Low-level window management and overlay functionality
- **Async/Await**: Non-blocking network operations

## Value Proposition

The Elite Dangerous INARA Overlay provides:
- **Increased Trading Efficiency**: Reduces time spent researching trade routes
- **Enhanced Gameplay Experience**: Keeps players immersed in the game world
- **Data-Driven Decision Making**: Provides accurate, up-to-date market information
- **Community Integration**: Leverages the collective knowledge of the INARA community
- **Customizable Experience**: Adapts to individual player preferences and ship configurations

This overlay represents a significant quality-of-life improvement for Elite Dangerous traders, bridging the gap between in-game experience and community-driven market intelligence.
