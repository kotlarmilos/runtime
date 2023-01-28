#import <Foundation/Foundation.h>
#import "runtime.h"

int main(int argc, const char * argv[])
{
    @autoreleasepool {
        mono_ios_runtime_init();
		NSLog(@"Hello, World!");
	}	   
    return 0;    
}