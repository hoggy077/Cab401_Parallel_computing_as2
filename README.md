# CAB401 | Parallel Performance
In this assignment we were provided a selection of applications & languages to modify & improve. For this I chose a winforms C# application that performs frequency analysis.
The provided files includes the university provided application & the modified source.

In order to achieve a performace gain, I implemented numerous notable changes. The most notable of this is the replacement of the included Recursive FFT with a Radix-2 Fast Fourier Transform.
This was namely due to its recursive nature restricting the ability to reliably multi-thread. Instead Radix-2 was implemented as it provides an in-thread iterative method of performing the same operation with only caveat being that the resulting output is not in the correct order. To get around this performantly however, a lookup table is generated prior based on the segment size allowing for each resulting segment to be re-order by simplying referring to said table.
Additionally, to allow larger scale proccessing, the primary function that initially called the recursive FFT was altered to a Task Queue & Data Queue format in which all Tasks are provided with access to the thread-safe Queue of "Work". Furthermore, all segments of audio are encapsulated in an additional *struct* object which maintains the segments position during proccessing, allowing the results to be placed in the output array without the possibility of incorrect ordering.


### Processor
Intel Core i5 - 9600K (Coffee Lake-S)
 - 6 Physical with 6 Threads
 - (At the time of testing) 4100Mhz
 - L1 
	 - Instruction
		 - 6×32 KBytes
		 - TLB : 2MB/4MB, Fully associative, 8 entries
	 - Data
		 - 6×32 KBytes
		 - TLB : 4KB pages, 4-way set associative, 64 entries
 - L2 : 6×256 KBytes
 - L3 : 9MBytes