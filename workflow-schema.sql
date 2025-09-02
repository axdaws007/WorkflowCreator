-- PAWS WORKFLOW SYSTEM SCHEMA
-- Instructions for generating INSERT statements:
-- 1. Always use NEWID() for uniqueidentifier columns
-- 2. ActivityIDs should be sequential starting from 1 for each workflow
-- 3. Respect foreign key relationships - insert in order: ProcessTemplate → Activity → ActivityTransition

-- ================================================
-- WORKFLOW PROCESS MASTER TABLE
-- ================================================
-- One row per workflow definition
CREATE TABLE [paws].[PAWSProcessTemplate](
	[processTemplateID] [uniqueidentifier] NOT NULL PRIMARY KEY, -- Use NEWID() when inserting
	[title] [nvarchar](100) NOT NULL,  -- Workflow name (e.g., 'Purchase Order Approval', 'Employee Onboarding')
	[IsArchived] [bit] NOT NULL, -- 0=Active, 1=Archived (default: 0)
	[ProcessSeed] [int] NULL, -- Starting number for process instances (default: 0)
	[ReassignEnabled] [bit] NOT NULL, -- 0=No reassignment, 1=Allow reassignment (default: 0)
	[ReassignCapabilityID] [int] NULL -- Reference to capability/permission system (default: NULL)
);

-- ================================================
-- WORKFLOW ACTIVITIES (STEPS)
-- ================================================
-- One row per step in the workflow
CREATE TABLE [paws].[PAWSActivity](
	[activityID] [int] NOT NULL PRIMARY KEY, -- Sequential ID within workflow (start from 1)
	[title] [nvarchar](100) NOT NULL, -- Step name (e.g., 'Manager Review', 'Submit Request')
	[description] [nvarchar](500) NULL, -- Detailed description of what happens in this step
	[processTemplateID] [uniqueidentifier] NOT NULL, -- FK to PAWSProcessTemplate.processTemplateID
	[DefaultOwnerRoleID] [uniqueidentifier] NULL, -- Role/User who handles this step (default: NULL)
	[IsRemoved] [bit] NOT NULL, -- 0=Active, 1=Removed (default: 0)
	[SignoffText] [nvarchar](max) NULL, -- Text shown for approval/signoff (default: NULL)
	[ShowSignoffText] [bit] NOT NULL, -- 0=Hide, 1=Show signoff text (default: 0)
	[RequirePassword] [bit] NOT NULL -- 0=No password, 1=Require password for signoff (default: 0)
);

-- ================================================
-- ACTIVITY STATUS LOOKUP TABLE
-- ================================================
-- Predefined statuses that trigger transitions
-- IMPORTANT: These are system-wide statuses, typically pre-populated:
-- Common values: 1='Approved', 2='Rejected', 3='Submit Draft', 4='Rejected', 5='Withdrawn', 6='Submit for Review', 7='Review and Close'
CREATE TABLE [paws].[PAWSActivityStatus](
	[ActivityStatusID] [int] NOT NULL PRIMARY KEY,
	[title] [nvarchar](100) NOT NULL
);

-- ================================================
-- WORKFLOW TRANSITIONS (FLOW LOGIC)
-- ================================================
-- Defines how activities connect and flow conditions
CREATE TABLE [paws].[PAWSActivityTransition](
	[ActivityTransitionID] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY, -- Auto-generated
	[SourceActivityID] [int] NULL, -- FK to PAWSActivity.activityID (NULL = workflow start)
	[DestinationActivityID] [int] NULL, -- FK to PAWSActivity.activityID (NULL = workflow end)
	[TriggerStatusID] [int] NOT NULL, -- FK to PAWSActivityStatus.ActivityStatusID
	[Operator] [int] NOT NULL, -- 0=Default/Normal transition (default: 0)
	[TransitionType] [int] NOT NULL, -- 1=Forward, 2=Backward/Regress
	[IsCommentRequired] [bit] NOT NULL, -- 0=Optional comment, 1=Comment required (default: 0)
	[DestinationOwnerRequired] [int] NOT NULL, -- 0=No owner change, 1=Must specify owner (default: 0)
	[DestinationTransitionGroup] [uniqueidentifier] NULL, -- Owner Group ID (default: NULL)
	[MUTHandler] [nvarchar](255) NULL, -- Custom handler/script (default: NULL)
	[MUTTags] [nvarchar](max) NULL -- Metadata tags (default: NULL)
);
