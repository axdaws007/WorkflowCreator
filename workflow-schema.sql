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

-- Standard activity statuses (reference data - usually already exists)
-- INSERT [paws].[PAWSActivityStatus] ([ActivityStatusID], [title]) VALUES 
-- (1, N'Pending')
-- (2, N'Submitted for Approval')
-- (3, N'Approved')
-- (4, N'Repaired')
-- (5, N'Withdrawn')
-- (6, N'Not Repaired')
-- (7, N'Rejected')
-- (8, N'Damage Changed')
-- (9, N'Repair Changed')
-- (10, N'Repaired via Other Repair')
-- (11, N'Repair Logged')
-- (12, N'ADF')
-- (13, N'Damage Logged')
-- (14, N'Damage Concessed')
-- (15, N'Damage Repair Decision changed')
-- (16, N'Repair Required')
-- (17, N'Submit Draft')
-- (18, N'Submit for Review')
-- (19, N'Review and Close')

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

-- ================================================
-- IMPORTANT NOTES FOR INSERT GENERATION:
-- ================================================
-- 1. INSERT ORDER: PAWSProcessTemplate → PAWSActivity → PAWSActivityTransition
-- 2. For each workflow, activities typically numbered sequentially (1, 2, 3...)
-- 3. The first row for the PAWSActivityTransition table should have [SourceActivityID] = NULL and a TriggerStatusID = 1 (Pending).
-- 4. Common transition patterns:
--    - Linear: Activity 1 → 2 → 3 → End
--    - Approval: Activity → (Approved → Next) OR (Rejected → Previous/End)
--    - Parallel: Activity → Multiple simultaneous activities → Convergence
-- 5. TriggerStatusID common mappings:
--    - 1 (Approved): Move to next step
--    - 2 (Rejected): Return to previous or end
--    - 3 (Submit): Initial submission to start workflow
--    - 4 (Complete): Finish current activity
-- 6. TransitionType: 1=Forward progression, 2=Backward (for rework/rejection)

-- ================================================
-- EXAMPLE WORKFLOW PATTERN:
-- ================================================
-- Simple Approval Workflow:
-- 1. Submit Request (Activity 1)
--    → Submitted for Approval (Status 2) → Manager Review (Activity 2)
-- 2. Manager Review (Activity 2)
--    → Approved (Status 3) → Complete (End)
--    → Rejected (Status 7) → Submit Request (Activity 1)

-- Complex Approval with Multiple Levels:
-- 1. Submit → Manager Review
-- 2. Manager Review → Approved → Director Review
-- 2. Manager Review → Rejected → Submit (with comments)
-- 3. Director Review → Approved → Finance Review
-- 3. Director Review → Rejected → Manager Review
-- 4. Finance Review → Approved → Complete
-- 4. Finance Review → Rejected → Director Review