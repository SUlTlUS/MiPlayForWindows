/*++

Copyright (c) Microsoft Corporation All Rights Reserved

Module Name:

    endpoints.h

Abstract:

    Node and Pin numbers and other common definitions for simple audio sample.
--*/

#ifndef _VIRTUALAUDIODRIVER_ENDPOINTS_H_
#define _VIRTUALAUDIODRIVER_ENDPOINTS_H_

// Name Guid
// {C6466A20-A8DF-45E3-995A-2544E654C8CC}
#define STATIC_NAME_SIMPLE_AUDIO_SAMPLE\
    0xc6466a20, 0xa8df, 0x45e3, 0x99, 0x5a, 0x25, 0x44, 0xe6, 0x54, 0xc8, 0xcc
DEFINE_GUIDSTRUCT("C6466A20-A8DF-45E3-995A-2544E654C8CC", NAME_SIMPLE_AUDIO_SAMPLE);
#define NAME_SIMPLE_AUDIO_SAMPLE DEFINE_GUIDNAMED(NAME_SIMPLE_AUDIO_SAMPLE)

//----------------------------------------------------
// New defines for the render endpoints.
//----------------------------------------------------

// Default pin instances.
#define MAX_INPUT_SYSTEM_STREAMS        1

// Wave pins - no mix, no offload
enum 
{
    KSPIN_WAVE_RENDER3_SINK_SYSTEM = 0, 
    KSPIN_WAVE_RENDER3_SOURCE
};

// Wave pins - offloading is NOT supported.
enum 
{
    KSPIN_WAVE_RENDER2_SINK_SYSTEM = 0, 
    KSPIN_WAVE_RENDER2_SINK_LOOPBACK, 
    KSPIN_WAVE_RENDER2_SOURCE
};

// Wave Topology nodes - offloading is NOT supported.
enum 
{
    KSNODE_WAVE_SUM = 0,
    KSNODE_WAVE_VOLUME,
    KSNODE_WAVE_MUTE,
    KSNODE_WAVE_PEAKMETER
};

// Topology pins.
enum
{
    KSPIN_TOPO_WAVEOUT_SOURCE = 0,
    KSPIN_TOPO_LINEOUT_DEST,
};

// Topology nodes.
enum
{
    KSNODE_TOPO_WAVEOUT_VOLUME = 0,
    KSNODE_TOPO_WAVEOUT_MUTE,
    KSNODE_TOPO_WAVEOUT_PEAKMETER
};

//----------------------------------------------------
// New defines for the capture endpoints.
//----------------------------------------------------

// Default pin instances.
#define MAX_INPUT_STREAMS           1       // Number of capture streams.

// Wave pins
enum 
{
    KSPIN_WAVE_BRIDGE = 0,
    KSPIN_WAVEIN_HOST,
};

// Wave Topology nodes.
enum 
{
    KSNODE_WAVE_ADC = 0
};

// Wave Topology nodes.
enum 
{
    KSNODE_WAVE_DAC = 0
};

// topology pins.
enum
{
    KSPIN_TOPO_MIC_ELEMENTS,
    KSPIN_TOPO_BRIDGE
};

// topology nodes.
enum
{
    KSNODE_TOPO_VOLUME,
    KSNODE_TOPO_MUTE,
    KSNODE_TOPO_PEAKMETER
};

// data format attribute range definitions.
static
KSATTRIBUTE PinDataRangeSignalProcessingModeAttribute =
{
    sizeof(KSATTRIBUTE),
    0,
    STATICGUIDOF(KSATTRIBUTEID_AUDIOSIGNALPROCESSING_MODE),
};

static
PKSATTRIBUTE PinDataRangeAttributes[] =
{
    &PinDataRangeSignalProcessingModeAttribute,
};

static
KSATTRIBUTE_LIST PinDataRangeAttributeList =
{
    ARRAYSIZE(PinDataRangeAttributes),
    PinDataRangeAttributes,
};

#endif // _VIRTUALAUDIODRIVER_ENDPOINTS_H_
